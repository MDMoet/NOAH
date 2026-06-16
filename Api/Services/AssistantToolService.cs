using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Api.Interfaces;
using Application.Interfaces;
using Application.Models;
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
    ILocationsService locationsService,
    IMileageService mileageService,
    IPlanningService planningService,
    TimeProvider timeProvider)
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
        "show me",
        "search for",
        "search",
        "find me",
        "find",
        "look up",
        "lookup"
    ];

    private static readonly string[] NotePrefixes =
    [
        "create a note",
        "create note",
        "add a note",
        "add note",
        "save a note",
        "save note",
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
        IReadOnlyList<AssistantContextSearchResult> searchResults =
            ShouldSearchForContext(request.Input)
                ? await SearchContextAsync(request.Input, ContextSearchResultLimit, cancellationToken)
                : Array.Empty<AssistantContextSearchResult>();

        return new AssistantPromptContext(
            timeProvider.GetUtcNow(),
            request.CurrentLocation,
            searchResults);
    }

    /// <summary>
    /// Attempts to execute a concrete NOAH tool action for the user message.
    /// </summary>
    /// <param name="request">The action request containing the command and interaction id.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The action result, or a not-handled result when the LLM should answer instead.</returns>
    public async Task<AssistantToolActionResult> TryExecuteAsync(
        AssistantToolActionRequest request,
        CancellationToken cancellationToken = default)
    {
        string input = request.Command.Input.Trim();

        if (TryGetCommandText(input, ReverseGeocodePrefixes, out string reverseGeocodeText))
        {
            return await ReverseGeocodeAsync(reverseGeocodeText, request.Command.CurrentLocation, cancellationToken);
        }

        if (TryGetCommandText(input, GeocodePrefixes, out string geocodeQuery))
        {
            return await GeocodeAsync(geocodeQuery, cancellationToken);
        }

        if (TryGetCommandText(input, NearbyPlacesPrefixes, out string nearbyQuery))
        {
            return await FindNearbyPlacesAsync(nearbyQuery, request.Command.CurrentLocation, cancellationToken);
        }

        if (TryGetCommandText(input, DistancePrefixes, out string distanceText))
        {
            return CalculateDistance(distanceText, request.Command.CurrentLocation);
        }

        if (TryGetCommandText(input, SaveLocationPrefixes, out string locationText))
        {
            return await SaveLocationAsync(locationText, request.Command.CurrentLocation, cancellationToken);
        }

        if (TryGetCommandText(input, MileagePrefixes, out string mileageText))
        {
            return await CreateMileageEntryAsync(mileageText, request.Command.CurrentLocation, cancellationToken);
        }

        if (StartsWithAny(input, PlanningPrefixes))
        {
            return await ShowPlanningAsync(input, cancellationToken);
        }

        if (TryGetCommandText(input, CalculatePrefixes, out string expression))
        {
            AssistantToolActionResult calculationResult = CalculateValue(expression);

            if (calculationResult.WasHandled)
            {
                return calculationResult;
            }
        }

        if (TryGetCommandText(input, SearchPrefixes, out string searchQuery))
        {
            return await SearchAsync(searchQuery, cancellationToken);
        }

        if (TryGetCommandText(input, NotePrefixes, out string noteText))
        {
            return await CreateNoteAsync(noteText, request, cancellationToken);
        }

        if (TryGetCommandText(input, TaskPrefixes, out string taskText))
        {
            return await CreateTaskAsync(taskText, cancellationToken);
        }

        if (TryGetCommandText(input, ReminderPrefixes, out string reminderText))
        {
            return await CreateReminderAsync(reminderText, request.Command.RequestedAtUtc, cancellationToken);
        }

        return AssistantToolActionResult.NotHandled;
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

    private async Task<AssistantToolActionResult> CreateNoteAsync(
        string noteText,
        AssistantToolActionRequest actionRequest,
        CancellationToken cancellationToken)
    {
        string content = NormalizeCommandBody(noteText);

        if (string.IsNullOrWhiteSpace(content))
        {
            return AssistantToolActionResult.NotHandled;
        }

        string title = CreateTitle(content, "New note");
        NoteDto note = await notesService.CreateNoteAsync(
            new CreateNoteRequest(
                title,
                content,
                actionRequest.Command.InputMode == AssistantInputModeDto.Voice,
                actionRequest.InteractionId),
            cancellationToken);

        return Handled(
            AssistantActionTypeDto.CreateNote,
            $"Created note \"{note.Title}\".",
            note.Id,
            "Note");
    }

    private async Task<AssistantToolActionResult> CreateTaskAsync(
        string taskText,
        CancellationToken cancellationToken)
    {
        string title = NormalizeCommandBody(taskText);

        if (string.IsNullOrWhiteSpace(title))
        {
            return AssistantToolActionResult.NotHandled;
        }

        TaskPriorityDto priority = ParseTaskPriority(title);
        DateTimeOffset? dueAtUtc = ParseLooseDueDate(title);

        TaskItemDto taskItem = await tasksService.CreateTaskItemAsync(
            new CreateTaskItemRequest(
                CreateTitle(CleanTaskTitle(title), "New task"),
                null,
                priority,
                dueAtUtc,
                dueAtUtc.HasValue ? DateOnly.FromDateTime(dueAtUtc.Value.UtcDateTime) : null),
            cancellationToken);

        string dueText = taskItem.DueAtUtc.HasValue
            ? $" Due at {taskItem.DueAtUtc.Value:u}."
            : string.Empty;

        return Handled(
            AssistantActionTypeDto.CreateTask,
            $"Created task \"{taskItem.Title}\".{dueText}",
            taskItem.Id,
            "Task");
    }

    private async Task<AssistantToolActionResult> CreateReminderAsync(
        string reminderText,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        string body = NormalizeCommandBody(reminderText);

        if (string.IsNullOrWhiteSpace(body))
        {
            return AssistantToolActionResult.NotHandled;
        }

        DateTimeOffset triggerAtUtc = ParseReminderTriggerAtUtc(body, requestedAtUtc.ToUniversalTime());
        string title = CreateTitle(CleanReminderTitle(body), "New reminder");

        ReminderDto reminder = await remindersService.CreateReminderAsync(
            new CreateReminderRequest(
                title,
                body,
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
            $"Created reminder \"{reminder.Title}\" for {triggerAtUtc:u}.",
            reminder.Id,
            "Reminder");
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

        return searchResponse.Results
            .Select(MapToContextSearchResult)
            .ToList();
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
        string normalizedInput = input.Trim();

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
        foreach (string prefix in prefixes.OrderByDescending(prefix => prefix.Length))
        {
            if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            commandText = NormalizeCommandBody(input[prefix.Length..]);
            return true;
        }

        commandText = string.Empty;
        return false;
    }

    private static bool StartsWithAny(string input, IReadOnlyList<string> prefixes)
    {
        return prefixes.Any(prefix => input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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
        return value.Trim().TrimStart(':', '-', '.', ' ').Trim();
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

    private DateTimeOffset? ParseLooseDueDate(string value)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();

        if (ContainsAny(value, "tomorrow"))
        {
            return new DateTimeOffset(nowUtc.UtcDateTime.Date.AddDays(1).AddHours(9), TimeSpan.Zero);
        }

        if (ContainsAny(value, "today"))
        {
            return nowUtc.AddHours(1);
        }

        if (ContainsAny(value, "next week"))
        {
            return new DateTimeOffset(nowUtc.UtcDateTime.Date.AddDays(7).AddHours(9), TimeSpan.Zero);
        }

        return null;
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

    private static string CleanTaskTitle(string value)
    {
        return Regex.Replace(
                value,
                @"\b(today|tomorrow|next week|urgent|important|high priority|low priority|not urgent)\b",
                string.Empty,
                RegexOptions.IgnoreCase)
            .Trim();
    }

    private static string CleanReminderTitle(string value)
    {
        string cleanedValue = Regex.Replace(
            value,
            @"\bin\s+\d+\s*(minute|minutes|hour|hours|day|days)\b",
            string.Empty,
            RegexOptions.IgnoreCase);

        cleanedValue = Regex.Replace(
            cleanedValue,
            @"\b(today|tomorrow)\b",
            string.Empty,
            RegexOptions.IgnoreCase);

        return cleanedValue.Trim();
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

    private sealed record AssistantSearchCriteria(
        string Query,
        IReadOnlyList<SearchResultTypeDto> Types);
}
