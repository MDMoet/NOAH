using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Api.Interfaces;
using Application.Interfaces;
using Application.Models;
using Microsoft.Extensions.Logging;
using NOAH.Contracts.Assistant;
using NOAH.Contracts.Common;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Locations;
using NOAH.Contracts.Mileage;
using NOAH.Contracts.Notes;
using NOAH.Contracts.Planning;
using NOAH.Contracts.Reminders;
using NOAH.Contracts.Search;
using NOAH.Contracts.Tasks;

namespace Api.Services;

/// <summary>
/// Provides assistant tools backed by existing NOAH application services.
/// </summary>
public sealed class AssistantToolService(
    ISearchService searchService,
    INotesService notesService,
    ITasksService tasksService,
    IRemindersService remindersService,
    IAssistantMemoryService assistantMemoryService,
    ILocationsService locationsService,
    IMileageService mileageService,
    IPlanningService planningService,
    TimeProvider timeProvider,
    ILogger<AssistantToolService> logger)
    : IAssistantToolService
{
    private const int ContextSearchResultLimit = 5;
    private const int ActionSearchResultLimit = 10;
    private const int MaximumGeneratedTitleLength = 80;
    private const double DefaultNearbyRadiusKilometers = 2;
    private const string DefaultTimeZoneId = "Europe/Amsterdam";

    private static readonly SearchResultTypeDto[] DefaultAssistantSearchTypes =
    [
        SearchResultTypeDto.Task,
        SearchResultTypeDto.Note,
        SearchResultTypeDto.Reminder,
        SearchResultTypeDto.MileageEntry,
        SearchResultTypeDto.SavedLocation
    ];

    private static readonly string[] SearchPrefixes =
    [
        "what do i have about",
        "search for",
        "search",
        "search my",
        "find my",
        "show my",
        "show me my"
    ];

    private static readonly Regex MemoryRecallIntentRegex = new(
        @"^(?:what\s+(?:preference|preferences)\b.*\bdo\s+i\s+have\b|which\b.*\bdo\s+i\s+prefer\b|what\s+do\s+i\s+prefer\b|what\s+do\s+you\s+remember(?:\s+about)?\b|what\s+have\s+you\s+(?:saved|stored|remembered)(?:\s+about)?\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const string PseudoToolNameRegexPattern =
        @"create[\s_]*(?:note|task|reminder|memory|mileage[\s_]*entry)|find[\s_]*nearby[\s_]*places|save[\s_]*(?:(?:my|this|current)[\s_]*)?location|calculate[\s_]*distance|geocode|reverse[\s_]*geocode|log[\s_]*mileage";

    private static readonly Regex PlannedToolSyntaxResponseRegex = new(
        $@"\b(?:{PseudoToolNameRegexPattern})\s*\((?:[^)\r\n]*)\)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly string[] NotePrefixes =
    [
        "create a note",
        "create note",
        "add a note",
        "add note",
        "make a note",
        "write a note",
        "save a note",
        "save note",
        "note down",
        "note",
        "note:"
    ];

    private static readonly string[] TaskPrefixes =
    [
        "create a task",
        "create task",
        "add a task",
        "add task",
        "new task",
        "task:"
    ];

    private static readonly string[] ReminderPrefixes =
    [
        "create a reminder",
        "create reminder",
        "add a reminder",
        "add reminder",
        "remind me to",
        "remind me",
        "reminder:"
    ];

    private static readonly string[] MemoryPrefixes =
    [
        "remember",
        "keep in mind",
        "for future reference",
        "save this for later"
    ];

    private static readonly string[] SaveLocationPrefixes =
    [
        "save current location as",
        "save my location as",
        "save location as",
        "save location",
        "add location",
        "location:"
    ];

    private static readonly string[] NearbyPlacesPrefixes =
    [
        "show me nearby",
        "show nearby",
        "show me places nearby",
        "find nearby",
        "nearby",
        "places nearby",
        "find places nearby",
        "find places"
    ];

    private static readonly string[] DistancePrefixes =
    [
        "calculate distance from",
        "distance from",
        "calculate distance to",
        "distance to",
        "distance"
    ];

    private static readonly string[] GeocodePrefixes =
    [
        "geocode",
        "where is",
        "find coordinates for"
    ];

    private static readonly string[] ReverseGeocodePrefixes =
    [
        "reverse geocode",
        "what address is",
        "where am i"
    ];

    private static readonly string[] MileagePrefixes =
    [
        "create mileage entry",
        "add mileage entry",
        "log mileage",
        "mileage:"
    ];

    private static readonly string[] PlanningPrefixes =
    [
        "show me day plan",
        "show my day plan",
        "show day plan",
        "day plan",
        "plan today",
        "plan tomorrow",
        "planning today",
        "planning tomorrow",
        "today plan",
        "tomorrow plan",
        "week plan",
        "upcoming plan",
        "overdue",
        "backlog"
    ];

    private static readonly string[] CalculatePrefixes =
    [
        "calculate",
        "what is"
    ];

    /// <summary>
    /// Builds contextual data for a user message before LLM execution.
    /// </summary>
    /// <param name="request">The assistant command request.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The prompt context for the message.</returns>
    public async Task<AssistantPromptContext> BuildContextAsync(
        AssistantCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        bool shouldSearchForContext = ShouldSearchForContext(request.Input);
        IReadOnlyList<AssistantContextSearchResult> searchResults =
            shouldSearchForContext
                ? await SearchContextAsync(request.Input, ContextSearchResultLimit, cancellationToken)
                : Array.Empty<AssistantContextSearchResult>();

        AssistantPromptContext promptContext = new()
        {
            CurrentDateTimeUtc = timeProvider.GetUtcNow(),
            CurrentLocation = request.CurrentLocation,
            SearchResults = searchResults
        };

        logger.LogInformation(
            "Built assistant tool context in {ElapsedMs} ms. Included search: {IncludedSearch}. Search results: {SearchResultCount}. Has current location: {HasCurrentLocation}.",
            GetElapsedMilliseconds(stopwatch),
            shouldSearchForContext,
            searchResults.Count,
            request.CurrentLocation != null);

        return promptContext;
    }

    /// <summary>
    /// Attempts to execute a direct NOAH utility action for the user message.
    /// </summary>
    /// <param name="request">The action request containing the command and interaction id.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The action result, or a not-handled result when semantic planning or normal LLM answering should continue.</returns>
    public async Task<AssistantToolActionResult> TryExecuteAsync(
        AssistantToolActionRequest request,
        CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string input = request.Command.Input.Trim();
        string routeName = "NotHandled";
        AssistantToolActionResult result = AssistantToolActionResult.NotHandled;

        if (TryGetCommandText(input, ReverseGeocodePrefixes, out string reverseGeocodeText))
        {
            routeName = "ReverseGeocode";
            result = await ReverseGeocodeAsync(reverseGeocodeText, request.Command.CurrentLocation, cancellationToken);
        }
        else if (TryGetCommandText(input, GeocodePrefixes, out string geocodeQuery))
        {
            routeName = "Geocode";
            result = await GeocodeAsync(geocodeQuery, cancellationToken);
        }
        else if (TryGetCommandText(input, NearbyPlacesPrefixes, out string nearbyQuery))
        {
            routeName = "FindNearbyPlaces";
            result = await FindNearbyPlacesAsync(nearbyQuery, request.Command.CurrentLocation, cancellationToken);
        }
        else if (TryGetCommandText(input, DistancePrefixes, out string distanceText))
        {
            routeName = "CalculateDistance";
            result = CalculateDistance(distanceText, request.Command.CurrentLocation);
        }
        else if (TryGetCommandText(input, SaveLocationPrefixes, out string locationText))
        {
            routeName = "SaveLocation";
            result = await SaveLocationAsync(locationText, request.Command.CurrentLocation, cancellationToken);
        }
        else if (TryGetCommandText(input, MileagePrefixes, out string mileageText))
        {
            routeName = "CreateMileageEntry";
            result = await CreateMileageEntryAsync(mileageText, request.Command.CurrentLocation, cancellationToken);
        }
        else if (StartsWithAny(input, PlanningPrefixes))
        {
            routeName = "ShowPlanning";
            result = await ShowPlanningAsync(input, cancellationToken);
        }
        else if (TryGetCommandText(input, CalculatePrefixes, out string expression))
        {
            AssistantToolActionResult calculationResult = CalculateValue(expression);

            if (calculationResult.WasHandled)
            {
                routeName = "CalculateValue";
                result = calculationResult;
            }
        }
        else if (IsMemoryRecallRequest(input))
        {
            routeName = "DeferredMemoryRecallToLlm";
        }
        else if (TryGetCommandText(input, SearchPrefixes, out string searchQuery))
        {
            routeName = "Search";
            result = await SearchAsync(searchQuery, cancellationToken);
        }
        // Content-creation commands are intentionally deferred to the structured planner so the
        // assistant can generate better titles, bodies, and reminder/task metadata semantically.
        else if (TryGetCommandText(input, NotePrefixes, out _))
        {
            routeName = "DeferredNoteToStructuredPlanner";
        }
        else if (TryGetCommandText(input, TaskPrefixes, out _))
        {
            routeName = "DeferredTaskToStructuredPlanner";
        }
        else if (TryGetCommandText(input, ReminderPrefixes, out _))
        {
            routeName = "DeferredReminderToStructuredPlanner";
        }
        else if (TryGetCommandText(input, MemoryPrefixes, out _))
        {
            routeName = "DeferredMemoryToStructuredPlanner";
        }

        logger.LogInformation(
            "Deterministic assistant tool routing completed in {ElapsedMs} ms. Route: {RouteName}. Handled: {WasHandled}. Action: {ActionType}.",
            GetElapsedMilliseconds(stopwatch),
            routeName,
            result.WasHandled,
            result.ActionType);

        return result;
    }

    /// <summary>
    /// Executes a structured tool plan produced by the LLM.
    /// </summary>
    /// <param name="request">The structured action request.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The action result, or a not-handled result when the plan is incomplete.</returns>
    public async Task<AssistantToolActionResult> ExecutePlannedActionAsync(
        AssistantPlannedToolActionRequest request,
        CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        AssistantToolActionResult result = request.Action.ActionType switch
        {
            AssistantActionTypeDto.CreateNote => await ExecutePlannedNoteAsync(request, cancellationToken),
            AssistantActionTypeDto.CreateTask => await ExecutePlannedTaskAsync(request, cancellationToken),
            AssistantActionTypeDto.CreateReminder => await ExecutePlannedReminderAsync(request, cancellationToken),
            AssistantActionTypeDto.CreateMemory => await ExecutePlannedMemoryAsync(request, cancellationToken),
            AssistantActionTypeDto.CreateMileageEntry => await CreateMileageEntryAsync(
                request.Action.Description ?? request.Action.Query ?? request.Action.Title ?? request.Command.Input,
                request.Command.CurrentLocation,
                cancellationToken),
            AssistantActionTypeDto.Search => await SearchAsync(
                request.Action.Query ?? request.Action.Title ?? request.Command.Input,
                cancellationToken),
            AssistantActionTypeDto.ShowDayPlan => await ExecutePlannedDayPlanAsync(request, cancellationToken),
            AssistantActionTypeDto.SaveLocation => await SaveLocationAsync(
                request.Action.Title ?? request.Action.Description ?? request.Command.Input,
                request.Command.CurrentLocation,
                cancellationToken),
            AssistantActionTypeDto.FindNearbyPlaces => await FindNearbyPlacesAsync(
                request.Action.Query ?? request.Action.Title ?? request.Command.Input,
                request.Command.CurrentLocation,
                cancellationToken),
            AssistantActionTypeDto.CalculateDistance => CalculateDistance(
                request.Action.Query ?? request.Action.Description ?? request.Command.Input,
                request.Command.CurrentLocation),
            _ => AssistantToolActionResult.NotHandled
        };

        logger.LogInformation(
            "Structured assistant tool action {RequestedActionType} completed in {ElapsedMs} ms. Handled: {WasHandled}. Result action: {ResultActionType}.",
            request.Action.ActionType,
            GetElapsedMilliseconds(stopwatch),
            result.WasHandled,
            result.ActionType);

        return result;
    }

    /// <summary>
    /// Creates a long-term memory item from a structured assistant plan.
    /// </summary>
    private async Task<AssistantToolActionResult> ExecutePlannedMemoryAsync(
        AssistantPlannedToolActionRequest request,
        CancellationToken cancellationToken)
    {
        string content = NormalizeCommandBody(request.Action.Description ?? request.Action.Title ?? string.Empty);

        if (string.IsNullOrWhiteSpace(content))
        {
            return AssistantToolActionResult.NotHandled;
        }

        string title = string.IsNullOrWhiteSpace(request.Action.Title)
            ? CreateTitle(content, "Saved memory")
            : request.Action.Title.Trim();
        AssistantMemoryItemDto memoryItem = await assistantMemoryService.CreateMemoryItemAsync(
            new CreateAssistantMemoryItemRequest(
                title,
                content,
                request.Action.Tags,
                request.Action.IsPinned,
                request.InteractionId,
                request.Command.ChatId),
            cancellationToken);

        return Handled(
            AssistantActionTypeDto.CreateMemory,
            GetPlannedResponseText(
                request.Action.ResponseText,
                $"I will remember \"{memoryItem.Title}\"."),
            memoryItem.Id,
            "AssistantMemoryItem");
    }

    private async Task<AssistantToolActionResult> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        string normalizedQuery = NormalizeCommandBody(query);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return AssistantToolActionResult.NotHandled;
        }

        AssistantSearchCriteria searchCriteria = BuildAssistantSearchCriteria(normalizedQuery);
        IReadOnlyList<AssistantContextSearchResult> searchResults =
            await SearchContextAsync(
                searchCriteria.Query,
                ActionSearchResultLimit,
                cancellationToken,
                searchCriteria.Types);

        string responseText = BuildSearchResponseText(searchCriteria, searchResults);

        return Handled(
            AssistantActionTypeDto.Search,
            responseText,
            searchResults: searchResults);
    }

    /// <summary>
    /// Creates a note from a structured assistant plan.
    /// </summary>
    private async Task<AssistantToolActionResult> ExecutePlannedNoteAsync(
        AssistantPlannedToolActionRequest request,
        CancellationToken cancellationToken)
    {
        string content = NormalizeCommandBody(request.Action.Description ?? request.Action.Title ?? string.Empty);

        if (string.IsNullOrWhiteSpace(content))
        {
            return AssistantToolActionResult.NotHandled;
        }

        string title = string.IsNullOrWhiteSpace(request.Action.Title)
            ? CreateTitle(content, "New note")
            : request.Action.Title.Trim();
        NoteDto note = await notesService.CreateNoteAsync(
            new CreateNoteRequest(
                title,
                content,
                request.Command.InputMode == AssistantInputModeDto.Voice,
                request.InteractionId),
            cancellationToken);

        return Handled(
            AssistantActionTypeDto.CreateNote,
            GetPlannedResponseText(
                request.Action.ResponseText,
                $"Created note \"{note.Title}\"."),
            note.Id,
            "Note");
    }

    private async Task<AssistantToolActionResult> ExecutePlannedTaskAsync(
        AssistantPlannedToolActionRequest request,
        CancellationToken cancellationToken)
    {
        string title = NormalizeCommandBody(request.Action.Title ?? string.Empty);

        if (string.IsNullOrWhiteSpace(title))
        {
            return AssistantToolActionResult.NotHandled;
        }

        DateTimeOffset? dueAtUtc = ParsePlannedDateTime(
            request.Action.ScheduledAt,
            request.Action.TimeZoneId);
        DateOnly? plannedFor = dueAtUtc.HasValue
            ? DateOnly.FromDateTime(dueAtUtc.Value.UtcDateTime)
            : ParsePlannedDate(request.Action.ScheduledAt, request.Action.TimeZoneId);
        string? description = BuildPlannedDescription(request.Action.Description, request.Action.EndsAt);
        TaskPriorityDto priority = request.Action.Priority ?? ParseTaskPriority(title);

        TaskItemDto taskItem = await tasksService.CreateTaskItemAsync(
            new CreateTaskItemRequest(
                title,
                description,
                priority,
                dueAtUtc,
                plannedFor),
            cancellationToken);

        if (!request.Action.CreateLinkedReminder)
        {
            string dueText = taskItem.DueAtUtc.HasValue
                ? $" Due at {taskItem.DueAtUtc.Value:u}."
                : string.Empty;

            return Handled(
                AssistantActionTypeDto.CreateTask,
                GetPlannedResponseText(
                    request.Action.ResponseText,
                    $"Created task \"{taskItem.Title}\".{dueText}"),
                taskItem.Id,
                "Task");
        }

        DateTimeOffset reminderAtUtc = ParsePlannedDateTime(
                request.Action.ReminderAt ?? request.Action.ScheduledAt,
                request.Action.TimeZoneId)
            ?? dueAtUtc
            ?? request.Command.RequestedAtUtc.ToUniversalTime().AddHours(1);
        string reminderTitle = NormalizeCommandBody(request.Action.ReminderTitle ?? taskItem.Title);
        string reminderMessage = NormalizeCommandBody(
            request.Action.ReminderMessage ??
            description ??
            taskItem.Title);

        ReminderDto reminder = await remindersService.CreateReminderAsync(
            new CreateReminderRequest(
                string.IsNullOrWhiteSpace(reminderTitle) ? taskItem.Title : reminderTitle,
                string.IsNullOrWhiteSpace(reminderMessage) ? taskItem.Title : reminderMessage,
                ReminderTriggerTypeDto.Time,
                true,
                reminderAtUtc,
                null,
                null,
                taskItem.Id,
                null,
                null),
            cancellationToken);

        return Handled(
            AssistantActionTypeDto.CreateTask,
            GetPlannedResponseText(
                request.Action.ResponseText,
                $"Created task \"{taskItem.Title}\" and linked reminder \"{reminder.Title}\" for {reminderAtUtc:u}."),
            taskItem.Id,
            "Task");
    }

    private async Task<AssistantToolActionResult> ExecutePlannedReminderAsync(
        AssistantPlannedToolActionRequest request,
        CancellationToken cancellationToken)
    {
        string title = NormalizeCommandBody(request.Action.ReminderTitle ?? request.Action.Title ?? string.Empty);
        string message = NormalizeCommandBody(
            request.Action.ReminderMessage ??
            request.Action.Description ??
            request.Action.Title ??
            string.Empty);

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message))
        {
            return AssistantToolActionResult.NotHandled;
        }

        DateTimeOffset triggerAtUtc = ParsePlannedDateTime(
                request.Action.ReminderAt ?? request.Action.ScheduledAt,
                request.Action.TimeZoneId)
            ?? ParseReminderTriggerAtUtc(
                message,
                request.Command.RequestedAtUtc.ToUniversalTime());
        string normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? CreateTitle(message, "New reminder")
            : title;

        ReminderDto reminder = await remindersService.CreateReminderAsync(
            new CreateReminderRequest(
                normalizedTitle,
                string.IsNullOrWhiteSpace(message) ? normalizedTitle : message,
                ReminderTriggerTypeDto.Time,
                true,
                triggerAtUtc,
                null,
                null,
                null,
                null,
                null),
            cancellationToken);

        return Handled(
            AssistantActionTypeDto.CreateReminder,
            GetPlannedResponseText(
                request.Action.ResponseText,
                $"Created reminder \"{reminder.Title}\" for {triggerAtUtc:u}."),
            reminder.Id,
            "Reminder");
    }

    private async Task<AssistantToolActionResult> ExecutePlannedDayPlanAsync(
        AssistantPlannedToolActionRequest request,
        CancellationToken cancellationToken)
    {
        DateOnly date = ParsePlannedDate(
                request.Action.ScheduledAt,
                request.Action.TimeZoneId)
            ?? ParsePlanDate(request.Command.Input);
        string timeZoneId = string.IsNullOrWhiteSpace(request.Action.TimeZoneId)
            ? DefaultTimeZoneId
            : request.Action.TimeZoneId;
        DayPlanDto dayPlan = await planningService.GetDayPlanAsync(
            date,
            timeZoneId,
            cancellationToken);

        return Handled(
            AssistantActionTypeDto.ShowDayPlan,
            GetPlannedResponseText(
                request.Action.ResponseText,
                BuildDayPlanResponseText(dayPlan)));
    }

    private async Task<AssistantToolActionResult> SaveLocationAsync(
        string locationText,
        GeoCoordinateDto? currentLocation,
        CancellationToken cancellationToken)
    {
        string body = NormalizeCommandBody(locationText);
        GeoCoordinateDto? coordinate = TryParseFirstCoordinate(body, out string coordinateText)
            ? ParseCoordinate(coordinateText)
            : currentLocation;

        if (coordinate == null)
        {
            return Handled(
                AssistantActionTypeDto.SaveLocation,
                "I need a currentLocation value or coordinates like \"52.3676, 4.9041\" to save a location.");
        }

        string name = RemoveCoordinateText(body, coordinateText);

        if (name.StartsWith("as ", StringComparison.OrdinalIgnoreCase))
        {
            name = name[3..].Trim();
        }

        name = string.IsNullOrWhiteSpace(name)
            ? "Saved location"
            : CreateTitle(name, "Saved location");

        SavedLocationDto savedLocation = await locationsService.SaveCurrentLocationAsync(
            new SaveCurrentLocationRequest(name, coordinate, null),
            cancellationToken);

        return Handled(
            AssistantActionTypeDto.SaveLocation,
            $"Saved location \"{savedLocation.Name}\" at {FormatCoordinate(savedLocation.Coordinate)}.",
            savedLocation.Id,
            "SavedLocation");
    }

    private async Task<AssistantToolActionResult> FindNearbyPlacesAsync(
        string nearbyText,
        GeoCoordinateDto? currentLocation,
        CancellationToken cancellationToken)
    {
        if (currentLocation == null)
        {
            return Handled(
                AssistantActionTypeDto.FindNearbyPlaces,
                "I need currentLocation to find nearby places.");
        }

        string query = NormalizeCommandBody(nearbyText);

        if (string.IsNullOrWhiteSpace(query))
        {
            query = "restaurant";
        }

        double radiusKilometers = ParseRadiusKilometers(query) ?? DefaultNearbyRadiusKilometers;
        query = CleanRadiusText(query);

        NearbyPlacesResponse nearbyPlaces = await locationsService.GetNearbyPlacesAsync(
            new NearbyPlacesRequest(currentLocation, query, radiusKilometers),
            cancellationToken);

        return Handled(
            AssistantActionTypeDto.FindNearbyPlaces,
            BuildNearbyPlacesResponseText(query, radiusKilometers, nearbyPlaces));
    }

    private AssistantToolActionResult CalculateDistance(
        string distanceText,
        GeoCoordinateDto? currentLocation)
    {
        string body = NormalizeCommandBody(distanceText);
        List<GeoCoordinateDto> coordinates = ParseCoordinates(body).ToList();

        GeoCoordinateDto? from = coordinates.Count >= 2
            ? coordinates[0]
            : currentLocation;
        GeoCoordinateDto? to = coordinates.Count >= 2
            ? coordinates[1]
            : coordinates.FirstOrDefault();

        if (from == null || to == null)
        {
            return Handled(
                AssistantActionTypeDto.CalculateDistance,
                "I need either two coordinates, or currentLocation plus one destination coordinate.");
        }

        DistanceResponse distance = locationsService.CalculateDistance(new DistanceRequest(from, to));

        return Handled(
            AssistantActionTypeDto.CalculateDistance,
            $"The distance is {distance.DistanceKilometers:0.##} km.");
    }

    private async Task<AssistantToolActionResult> GeocodeAsync(
        string geocodeText,
        CancellationToken cancellationToken)
    {
        string query = NormalizeCommandBody(geocodeText);

        if (string.IsNullOrWhiteSpace(query))
        {
            return AssistantToolActionResult.NotHandled;
        }

        GeocodeResponse geocodeResponse =
            await locationsService.GeocodeAsync(new GeocodeRequest(query), cancellationToken);

        return Handled(
            AssistantActionTypeDto.FindNearbyPlaces,
            BuildGeocodeResponseText(query, geocodeResponse));
    }

    private async Task<AssistantToolActionResult> ReverseGeocodeAsync(
        string reverseGeocodeText,
        GeoCoordinateDto? currentLocation,
        CancellationToken cancellationToken)
    {
        GeoCoordinateDto? coordinate = TryParseFirstCoordinate(reverseGeocodeText, out string coordinateText)
            ? ParseCoordinate(coordinateText)
            : currentLocation;

        if (coordinate == null)
        {
            return Handled(
                AssistantActionTypeDto.FindNearbyPlaces,
                "I need currentLocation or coordinates like \"52.3676, 4.9041\" to reverse geocode.");
        }

        ReverseGeocodeResponse? reverseGeocodeResponse =
            await locationsService.ReverseGeocodeAsync(new ReverseGeocodeRequest(coordinate), cancellationToken);

        string responseText = reverseGeocodeResponse == null
            ? $"I could not find an address for {FormatCoordinate(coordinate)}."
            : $"{FormatCoordinate(coordinate)} is {reverseGeocodeResponse.DisplayName}.";

        return Handled(
            AssistantActionTypeDto.FindNearbyPlaces,
            responseText);
    }

    private async Task<AssistantToolActionResult> CreateMileageEntryAsync(
        string mileageText,
        GeoCoordinateDto? currentLocation,
        CancellationToken cancellationToken)
    {
        string body = NormalizeCommandBody(mileageText);
        decimal? odometerReadingKm = ParseFirstDecimal(body);

        if (!odometerReadingKm.HasValue)
        {
            return Handled(
                AssistantActionTypeDto.CreateMileageEntry,
                "I need an odometer reading, for example: \"log mileage 222222 km\".");
        }

        decimal? tripDistanceKm = ParseLabeledDecimal(body, "trip", "distance");
        MileageEntryDto mileageEntry = await mileageService.CreateMileageEntryAsync(
            new CreateMileageEntryRequest(
                timeProvider.GetUtcNow(),
                odometerReadingKm.Value,
                tripDistanceKm,
                MileageEntrySourceDto.VoiceCommand,
                null,
                null,
                null,
                currentLocation,
                body),
            cancellationToken);

        string tripText = mileageEntry.TripDistanceKm.HasValue
            ? $" Trip distance: {mileageEntry.TripDistanceKm.Value:0.##} km."
            : string.Empty;

        return Handled(
            AssistantActionTypeDto.CreateMileageEntry,
            $"Logged mileage at {mileageEntry.OdometerReadingKm:0.##} km.{tripText}",
            mileageEntry.Id,
            "MileageEntry");
    }

    private async Task<AssistantToolActionResult> ShowPlanningAsync(
        string input,
        CancellationToken cancellationToken)
    {
        string normalizedInput = input.Trim();

        if (normalizedInput.StartsWith("overdue", StringComparison.OrdinalIgnoreCase))
        {
            PlanningItemsDto overdueItems =
                await planningService.GetOverdueItemsAsync(DefaultTimeZoneId, cancellationToken);

            return Handled(
                AssistantActionTypeDto.ShowDayPlan,
                BuildPlanningItemsResponseText("Overdue items", overdueItems));
        }

        if (normalizedInput.StartsWith("backlog", StringComparison.OrdinalIgnoreCase))
        {
            PlanningItemsDto backlogItems =
                await planningService.GetBacklogItemsAsync(DefaultTimeZoneId, cancellationToken);

            return Handled(
                AssistantActionTypeDto.ShowDayPlan,
                BuildPlanningItemsResponseText("Backlog", backlogItems));
        }

        if (normalizedInput.StartsWith("week plan", StringComparison.OrdinalIgnoreCase))
        {
            PlanningPeriodDto weekPlan = await planningService.GetWeekPlanAsync(
                ParsePlanDate(normalizedInput),
                DefaultTimeZoneId,
                cancellationToken);

            return Handled(
                AssistantActionTypeDto.ShowDayPlan,
                BuildPlanningPeriodResponseText("Week plan", weekPlan));
        }

        if (normalizedInput.StartsWith("upcoming plan", StringComparison.OrdinalIgnoreCase))
        {
            int days = ParseFirstInteger(normalizedInput) ?? 7;
            PlanningPeriodDto upcomingPlan = await planningService.GetUpcomingPlanAsync(
                Math.Clamp(days, 1, 31),
                DefaultTimeZoneId,
                cancellationToken);

            return Handled(
                AssistantActionTypeDto.ShowDayPlan,
                BuildPlanningPeriodResponseText("Upcoming plan", upcomingPlan));
        }

        DateOnly date = ParsePlanDate(normalizedInput);
        DayPlanDto dayPlan = await planningService.GetDayPlanAsync(
            date,
            DefaultTimeZoneId,
            cancellationToken);

        return Handled(
            AssistantActionTypeDto.ShowDayPlan,
            BuildDayPlanResponseText(dayPlan));
    }

    private static AssistantToolActionResult CalculateValue(string expression)
    {
        string normalizedExpression = NormalizeCommandBody(expression);
        Match match = Regex.Match(
            normalizedExpression,
            @"(?<left>-?\d+(?:[\.,]\d+)?)\s*(?<operator>\+|\-|\*|x|/)\s*(?<right>-?\d+(?:[\.,]\d+)?)",
            RegexOptions.IgnoreCase);

        if (!match.Success ||
            !TryParseDecimal(match.Groups["left"].Value, out decimal left) ||
            !TryParseDecimal(match.Groups["right"].Value, out decimal right))
        {
            return AssistantToolActionResult.NotHandled;
        }

        string op = match.Groups["operator"].Value.ToLowerInvariant();

        if (op == "/" && right == 0)
        {
            return Handled(
                AssistantActionTypeDto.CalculateValue,
                "I cannot divide by zero.");
        }

        decimal result = op switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" or "x" => left * right,
            "/" => left / right,
            _ => 0
        };

        return Handled(
            AssistantActionTypeDto.CalculateValue,
            $"{left:0.##} {op} {right:0.##} = {result:0.##}.");
    }

    private async Task<IReadOnlyList<AssistantContextSearchResult>> SearchContextAsync(
        string query,
        int take,
        CancellationToken cancellationToken,
        IReadOnlyList<SearchResultTypeDto>? types = null)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        AssistantSearchCriteria searchCriteria = types == null
            ? BuildAssistantSearchCriteria(query)
            : new AssistantSearchCriteria(query.Trim(), types);

        SearchResponse searchResponse = await searchService.SearchAsync(
            new SearchRequest(
                searchCriteria.Query,
                searchCriteria.Types,
                null,
                null,
                0,
                take),
            cancellationToken);

        IReadOnlyList<AssistantContextSearchResult> searchResults = searchResponse.Results
            .Select(MapToContextSearchResult)
            .ToList();

        logger.LogInformation(
            "Assistant context search completed in {ElapsedMs} ms. Query: {Query}. Types: {SearchTypes}. Results: {ResultCount}.",
            GetElapsedMilliseconds(stopwatch),
            searchCriteria.Query,
            FormatSearchTypes(searchCriteria.Types),
            searchResults.Count);

        return searchResults;
    }

    private static AssistantContextSearchResult MapToContextSearchResult(SearchResultDto searchResult)
    {
        return new AssistantContextSearchResult(
            searchResult.Id,
            searchResult.Type.ToString(),
            searchResult.Title,
            searchResult.Preview,
            searchResult.RelevantAtUtc);
    }

    private static bool ShouldSearchForContext(string input)
    {
        string normalizedInput = NormalizeCommandInput(input);

        return normalizedInput.Length >= 3 &&
               !StartsWithAny(normalizedInput, NotePrefixes) &&
               !StartsWithAny(normalizedInput, TaskPrefixes) &&
               !StartsWithAny(normalizedInput, ReminderPrefixes) &&
               !StartsWithAny(normalizedInput, SaveLocationPrefixes) &&
               !StartsWithAny(normalizedInput, NearbyPlacesPrefixes) &&
               !StartsWithAny(normalizedInput, DistancePrefixes) &&
               !StartsWithAny(normalizedInput, GeocodePrefixes) &&
               !StartsWithAny(normalizedInput, ReverseGeocodePrefixes) &&
               !StartsWithAny(normalizedInput, MileagePrefixes) &&
               !StartsWithAny(normalizedInput, PlanningPrefixes) &&
               !StartsWithAny(normalizedInput, CalculatePrefixes);
    }

    private static bool TryGetCommandText(
        string input,
        IReadOnlyList<string> prefixes,
        out string commandText)
    {
        string normalizedInput = NormalizeCommandInput(input);

        foreach (string prefix in prefixes.OrderByDescending(prefix => prefix.Length))
        {
            if (!HasPrefixMatch(normalizedInput, prefix))
            {
                continue;
            }

            commandText = NormalizeCommandBody(normalizedInput[prefix.Length..]);
            return true;
        }

        commandText = string.Empty;
        return false;
    }

    private static bool StartsWithAny(string input, IReadOnlyList<string> prefixes)
    {
        string normalizedInput = NormalizeCommandInput(input);
        return prefixes.Any(prefix => HasPrefixMatch(normalizedInput, prefix));
    }

    private static bool HasPrefixMatch(string input, string prefix)
    {
        if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (input.Length == prefix.Length || !char.IsLetterOrDigit(prefix[^1]))
        {
            return true;
        }

        char nextCharacter = input[prefix.Length];
        return char.IsWhiteSpace(nextCharacter) || char.IsPunctuation(nextCharacter);
    }

    private static string GetPlannedResponseText(string? plannedResponseText, string fallbackResponseText)
    {
        string normalizedResponseText = NormalizeCommandBody(plannedResponseText ?? string.Empty);

        return string.IsNullOrWhiteSpace(normalizedResponseText) ||
               PlannedToolSyntaxResponseRegex.IsMatch(normalizedResponseText)
            ? fallbackResponseText
            : normalizedResponseText;
    }

    private static AssistantToolActionResult Handled(
        AssistantActionTypeDto actionType,
        string responseText,
        Guid? relatedEntityId = null,
        string? relatedEntityType = null,
        IReadOnlyList<AssistantContextSearchResult>? searchResults = null)
    {
        return new AssistantToolActionResult(
            true,
            actionType,
            responseText,
            relatedEntityId,
            relatedEntityType,
            searchResults ?? Array.Empty<AssistantContextSearchResult>());
    }

    private static string NormalizeCommandBody(string value)
    {
        string normalizedValue = value.Trim().TrimStart(':', '-', '.', ' ').Trim();
        normalizedValue = Regex.Replace(
            normalizedValue,
            @"(?:\s*(?:for me|please)[\s!?,.:;]*)+$",
            string.Empty,
            RegexOptions.IgnoreCase);
        return normalizedValue.Trim();
    }

    private static string NormalizeCommandInput(string value)
    {
        string normalizedValue = value.Trim();
        normalizedValue = Regex.Replace(
            normalizedValue,
            @"^\s*(?:(?:hey|hi)\s+noah[\s,:\-]*)?(?:(?:please|can you|could you|would you|will you|can u|could u|would u)\s+)+",
            string.Empty,
            RegexOptions.IgnoreCase);

        return normalizedValue.TrimStart(':', '-', '.', ',', ' ').Trim();
    }

    private static bool IsMemoryRecallRequest(string input)
    {
        string normalizedInput = NormalizeCommandInput(input);
        return MemoryRecallIntentRegex.IsMatch(normalizedInput);
    }

    private static string BuildSearchResponseText(
        AssistantSearchCriteria searchCriteria,
        IReadOnlyList<AssistantContextSearchResult> searchResults)
    {
        string typeText = FormatSearchTypes(searchCriteria.Types);
        string queryText = string.IsNullOrWhiteSpace(searchCriteria.Query)
            ? typeText
            : $"\"{searchCriteria.Query}\" in {typeText}";

        if (searchResults.Count == 0)
        {
            return $"I did not find anything for {queryText}.";
        }

        StringBuilder responseBuilder = new();
        responseBuilder.AppendLine($"I found {searchResults.Count} result(s) for {queryText}:");

        foreach (AssistantContextSearchResult searchResult in searchResults.Take(5))
        {
            responseBuilder.Append("- ");
            responseBuilder.Append(searchResult.Type);
            responseBuilder.Append(": ");
            responseBuilder.Append(searchResult.Title);

            if (!string.IsNullOrWhiteSpace(searchResult.Preview))
            {
                responseBuilder.Append(" - ");
                responseBuilder.Append(searchResult.Preview);
            }

            responseBuilder.AppendLine();
        }

        return responseBuilder.ToString().Trim();
    }

    private static AssistantSearchCriteria BuildAssistantSearchCriteria(string query)
    {
        string normalizedQuery = NormalizeCommandBody(query);
        List<SearchResultTypeDto> types = [];

        AddSearchTypeWhenMatched(types, normalizedQuery, SearchResultTypeDto.Note, @"\bnotes?\b");
        AddSearchTypeWhenMatched(types, normalizedQuery, SearchResultTypeDto.Task, @"\btasks?\b");
        AddSearchTypeWhenMatched(types, normalizedQuery, SearchResultTypeDto.Reminder, @"\breminders?\b");
        AddSearchTypeWhenMatched(types, normalizedQuery, SearchResultTypeDto.MileageEntry, @"\b(mileage|odometer|mileage entries?)\b");
        AddSearchTypeWhenMatched(types, normalizedQuery, SearchResultTypeDto.SavedLocation, @"\b(saved locations?|locations?)\b");
        AddSearchTypeWhenMatched(
            types,
            normalizedQuery,
            SearchResultTypeDto.AssistantInteraction,
            @"\b(assistant interactions?|assistant history|conversation history)\b");

        IReadOnlyList<SearchResultTypeDto> selectedTypes = types.Count == 0
            ? DefaultAssistantSearchTypes
            : types.Distinct().ToArray();

        string cleanedQuery = CleanSearchQueryForTypes(normalizedQuery, selectedTypes);

        return new AssistantSearchCriteria(cleanedQuery, selectedTypes);
    }

    private static void AddSearchTypeWhenMatched(
        List<SearchResultTypeDto> types,
        string query,
        SearchResultTypeDto type,
        string pattern)
    {
        if (Regex.IsMatch(query, pattern, RegexOptions.IgnoreCase))
        {
            types.Add(type);
        }
    }

    private static string CleanSearchQueryForTypes(
        string query,
        IReadOnlyList<SearchResultTypeDto> types)
    {
        string cleanedQuery = query;

        if (types.Contains(SearchResultTypeDto.Note))
        {
            cleanedQuery = Regex.Replace(cleanedQuery, @"\bnotes?\b", " ", RegexOptions.IgnoreCase);
        }

        if (types.Contains(SearchResultTypeDto.Task))
        {
            cleanedQuery = Regex.Replace(cleanedQuery, @"\btasks?\b", " ", RegexOptions.IgnoreCase);
        }

        if (types.Contains(SearchResultTypeDto.Reminder))
        {
            cleanedQuery = Regex.Replace(cleanedQuery, @"\breminders?\b", " ", RegexOptions.IgnoreCase);
        }

        if (types.Contains(SearchResultTypeDto.MileageEntry))
        {
            cleanedQuery = Regex.Replace(cleanedQuery, @"\b(mileage|odometer|mileage entries?)\b", " ", RegexOptions.IgnoreCase);
        }

        if (types.Contains(SearchResultTypeDto.SavedLocation))
        {
            cleanedQuery = Regex.Replace(cleanedQuery, @"\b(saved locations?|locations?)\b", " ", RegexOptions.IgnoreCase);
        }

        if (types.Contains(SearchResultTypeDto.AssistantInteraction))
        {
            cleanedQuery = Regex.Replace(
                cleanedQuery,
                @"\b(assistant interactions?|assistant history|conversation history)\b",
                " ",
                RegexOptions.IgnoreCase);
        }

        cleanedQuery = Regex.Replace(cleanedQuery, @"\b(about|related to|for|in|from|my|all)\b", " ", RegexOptions.IgnoreCase);
        cleanedQuery = NormalizeWhitespace(cleanedQuery);

        return cleanedQuery.Trim(' ', '?', '.', ':', '-');
    }

    private static string FormatSearchTypes(IReadOnlyList<SearchResultTypeDto> types)
    {
        return string.Join(
            ", ",
            types.Select(type => type switch
            {
                SearchResultTypeDto.MileageEntry => "mileage entries",
                SearchResultTypeDto.SavedLocation => "saved locations",
                SearchResultTypeDto.AssistantInteraction => "assistant interactions",
                _ => type.ToString().ToLowerInvariant() + "s"
            }));
    }

    private static string BuildNearbyPlacesResponseText(
        string query,
        double radiusKilometers,
        NearbyPlacesResponse nearbyPlaces)
    {
        if (nearbyPlaces.Places.Count == 0)
        {
            return $"I found no nearby places for \"{query}\" within {radiusKilometers:0.##} km.";
        }

        StringBuilder responseBuilder = new();
        responseBuilder.AppendLine($"I found {nearbyPlaces.Places.Count} nearby place(s) for \"{query}\" within {radiusKilometers:0.##} km:");

        foreach (NearbyPlaceDto place in nearbyPlaces.Places.Take(5))
        {
            responseBuilder.Append("- ");
            responseBuilder.Append(place.Name);
            responseBuilder.Append($" ({place.DistanceKilometers:0.##} km)");

            if (!string.IsNullOrWhiteSpace(place.Address))
            {
                responseBuilder.Append(" - ");
                responseBuilder.Append(place.Address);
            }

            responseBuilder.AppendLine();
        }

        return responseBuilder.ToString().Trim();
    }

    private static string BuildGeocodeResponseText(string query, GeocodeResponse geocodeResponse)
    {
        if (geocodeResponse.Results.Count == 0)
        {
            return $"I found no coordinates for \"{query}\".";
        }

        StringBuilder responseBuilder = new();
        responseBuilder.AppendLine($"I found {geocodeResponse.Results.Count} location result(s) for \"{query}\":");

        foreach (GeocodeResultDto result in geocodeResponse.Results.Take(5))
        {
            responseBuilder.Append("- ");
            responseBuilder.Append(result.DisplayName);
            responseBuilder.Append(" at ");
            responseBuilder.Append(FormatCoordinate(result.Coordinate));
            responseBuilder.AppendLine();
        }

        return responseBuilder.ToString().Trim();
    }

    private static string BuildDayPlanResponseText(DayPlanDto dayPlan)
    {
        return $"Plan for {dayPlan.Date:yyyy-MM-dd}: {dayPlan.Tasks.Count} task(s), {dayPlan.Reminders.Count} reminder(s).";
    }

    private static string BuildPlanningPeriodResponseText(string title, PlanningPeriodDto planningPeriod)
    {
        int taskCount = planningPeriod.Days.Sum(day => day.Tasks.Count);
        int reminderCount = planningPeriod.Days.Sum(day => day.Reminders.Count);

        return $"{title} {planningPeriod.StartsOn:yyyy-MM-dd} to {planningPeriod.EndsOn:yyyy-MM-dd}: {taskCount} task(s), {reminderCount} reminder(s).";
    }

    private static string BuildPlanningItemsResponseText(string title, PlanningItemsDto planningItems)
    {
        return $"{title}: {planningItems.Tasks.Count} task(s), {planningItems.Reminders.Count} reminder(s).";
    }

    private static string CreateTitle(string value, string fallbackTitle)
    {
        string normalizedValue = NormalizeWhitespace(value);

        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return fallbackTitle;
        }

        int sentenceEndIndex = normalizedValue.IndexOfAny(['.', '!', '?']);
        string title = sentenceEndIndex > 0
            ? normalizedValue[..sentenceEndIndex]
            : normalizedValue;

        return title.Length <= MaximumGeneratedTitleLength
            ? title
            : title[..MaximumGeneratedTitleLength].Trim();
    }

    private static TaskPriorityDto ParseTaskPriority(string value)
    {
        if (ContainsAny(value, "urgent", "important", "high priority"))
        {
            return TaskPriorityDto.High;
        }

        if (ContainsAny(value, "low priority", "not urgent"))
        {
            return TaskPriorityDto.Low;
        }

        return TaskPriorityDto.Normal;
    }

    private static DateTimeOffset ParseReminderTriggerAtUtc(
        string value,
        DateTimeOffset requestedAtUtc)
    {
        Match relativeMatch = Regex.Match(
            value,
            @"\bin\s+(?<amount>\d+)\s*(?<unit>minute|minutes|hour|hours|day|days)\b",
            RegexOptions.IgnoreCase);

        if (relativeMatch.Success &&
            int.TryParse(relativeMatch.Groups["amount"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
        {
            string unit = relativeMatch.Groups["unit"].Value.ToLowerInvariant();

            return unit switch
            {
                "minute" or "minutes" => requestedAtUtc.AddMinutes(amount),
                "hour" or "hours" => requestedAtUtc.AddHours(amount),
                "day" or "days" => requestedAtUtc.AddDays(amount),
                _ => requestedAtUtc.AddHours(1)
            };
        }

        if (ContainsAny(value, "tomorrow"))
        {
            return new DateTimeOffset(requestedAtUtc.UtcDateTime.Date.AddDays(1).AddHours(9), TimeSpan.Zero);
        }

        return requestedAtUtc.AddHours(1);
    }

    private DateTimeOffset? ParsePlannedDateTime(string? value, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalizedValue = value.Trim();

        if (DateTimeOffset.TryParse(
                normalizedValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsedOffsetDateTime))
        {
            return parsedOffsetDateTime.ToUniversalTime();
        }

        if (!DateTime.TryParse(
                normalizedValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime localDateTime))
        {
            return null;
        }

        TimeZoneInfo timeZone = ResolveTimeZone(timeZoneId);
        DateTime unspecifiedDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);

        return TimeZoneInfo.ConvertTimeToUtc(unspecifiedDateTime, timeZone);
    }

    private DateOnly? ParsePlannedDate(string? value, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, out DateOnly dateOnly))
        {
            return dateOnly;
        }

        DateTimeOffset? parsedDateTimeUtc = ParsePlannedDateTime(value, timeZoneId);

        return parsedDateTimeUtc.HasValue
            ? DateOnly.FromDateTime(parsedDateTimeUtc.Value.UtcDateTime)
            : null;
    }

    private static string? BuildPlannedDescription(string? description, string? endsAt)
    {
        string? normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : NormalizeWhitespace(description);

        if (string.IsNullOrWhiteSpace(endsAt))
        {
            return normalizedDescription;
        }

        string endText = $"Ends at {endsAt.Trim()}.";

        return string.IsNullOrWhiteSpace(normalizedDescription)
            ? endText
            : $"{normalizedDescription} {endText}";
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        try
        {
            return string.IsNullOrWhiteSpace(timeZoneId)
                ? TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId)
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
        }
    }

    private DateOnly ParsePlanDate(string value)
    {
        if (ContainsAny(value, "tomorrow"))
        {
            return DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime.AddDays(1));
        }

        Match explicitDateMatch = Regex.Match(value, @"\b\d{4}-\d{2}-\d{2}\b");

        if (explicitDateMatch.Success &&
            DateOnly.TryParseExact(explicitDateMatch.Value, "yyyy-MM-dd", out DateOnly explicitDate))
        {
            return explicitDate;
        }

        return DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
    }

    private static double? ParseRadiusKilometers(string value)
    {
        Match match = Regex.Match(
            value,
            @"\bwithin\s+(?<radius>\d+(?:[\.,]\d+)?)\s*(?<unit>km|kilometer|kilometers|m|meter|meters)\b",
            RegexOptions.IgnoreCase);

        if (!match.Success ||
            !double.TryParse(
                match.Groups["radius"].Value.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double radius))
        {
            return null;
        }

        string unit = match.Groups["unit"].Value.ToLowerInvariant();

        return unit is "m" or "meter" or "meters"
            ? radius / 1000
            : radius;
    }

    private static string CleanRadiusText(string value)
    {
        return Regex.Replace(
                value,
                @"\bwithin\s+\d+(?:[\.,]\d+)?\s*(km|kilometer|kilometers|m|meter|meters)\b",
                string.Empty,
                RegexOptions.IgnoreCase)
            .Trim();
    }

    private static decimal? ParseFirstDecimal(string value)
    {
        Match match = Regex.Match(value, @"-?\d+(?:[\.,]\d+)?");

        return match.Success && TryParseDecimal(match.Value, out decimal result)
            ? result
            : null;
    }

    private static decimal? ParseLabeledDecimal(string value, params string[] labels)
    {
        foreach (string label in labels)
        {
            Match match = Regex.Match(
                value,
                $@"\b{Regex.Escape(label)}\s+(?<value>-?\d+(?:[\.,]\d+)?)",
                RegexOptions.IgnoreCase);

            if (match.Success && TryParseDecimal(match.Groups["value"].Value, out decimal result))
            {
                return result;
            }
        }

        return null;
    }

    private static int? ParseFirstInteger(string value)
    {
        Match match = Regex.Match(value, @"\b\d+\b");

        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : null;
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        return decimal.TryParse(
            value.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static IEnumerable<GeoCoordinateDto> ParseCoordinates(string value)
    {
        foreach (Match match in Regex.Matches(
                     value,
                     @"(?<lat>-?\d+(?:[\.,]\d+)?)\s*,\s*(?<lon>-?\d+(?:[\.,]\d+)?)"))
        {
            GeoCoordinateDto? coordinate = ParseCoordinate(match.Value);

            if (coordinate != null)
            {
                yield return coordinate;
            }
        }
    }

    private static bool TryParseFirstCoordinate(string value, out string coordinateText)
    {
        Match match = Regex.Match(
            value,
            @"(?<lat>-?\d+(?:[\.,]\d+)?)\s*,\s*(?<lon>-?\d+(?:[\.,]\d+)?)");

        coordinateText = match.Success ? match.Value : string.Empty;

        return match.Success;
    }

    private static GeoCoordinateDto? ParseCoordinate(string value)
    {
        Match match = Regex.Match(
            value,
            @"(?<lat>-?\d+(?:[\.,]\d+)?)\s*,\s*(?<lon>-?\d+(?:[\.,]\d+)?)");

        if (!match.Success ||
            !double.TryParse(
                match.Groups["lat"].Value.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double latitude) ||
            !double.TryParse(
                match.Groups["lon"].Value.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double longitude) ||
            !double.IsFinite(latitude) ||
            !double.IsFinite(longitude) ||
            latitude is < -90 or > 90 ||
            longitude is < -180 or > 180)
        {
            return null;
        }

        return new GeoCoordinateDto(latitude, longitude);
    }

    private static string RemoveCoordinateText(string value, string coordinateText)
    {
        return string.IsNullOrWhiteSpace(coordinateText)
            ? value
            : value.Replace(coordinateText, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
    }

    private static string FormatCoordinate(GeoCoordinateDto coordinate)
    {
        return $"{coordinate.Latitude:0.######}, {coordinate.Longitude:0.######}";
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(
            " ",
            value.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static double GetElapsedMilliseconds(Stopwatch stopwatch)
    {
        return Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
    }

    private sealed record AssistantSearchCriteria(
        string Query,
        IReadOnlyList<SearchResultTypeDto> Types);
}
