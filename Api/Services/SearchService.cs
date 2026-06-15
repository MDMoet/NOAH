using System.Globalization;
using Api.Interfaces;
using Microsoft.EntityFrameworkCore;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Search;
using NOAH.Domain.Common;
using NOAH.Domain.Entities;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

/// <summary>
/// Searches across NOAH entities using lightweight in-application ranking.
/// </summary>
public sealed class SearchService(NoahDbContext noahDbContext) : ISearchService
{
    private const int DefaultTake = 25;
    private const int MaximumTake = 100;
    private const int MaximumPreviewLength = 240;

    /// <summary>
    /// Searches across supported NOAH entities.
    /// </summary>
    /// <param name="request">The search criteria.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The matching search results.</returns>
    public async Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        SearchOptions options = SearchOptions.FromRequest(request);
        string[] queryTerms = SplitQuery(options.Query);
        List<SearchDocument> documents = new();

        if (options.Includes(SearchResultTypeDto.Note))
        {
            documents.AddRange(await GetNoteDocumentsAsync(options, cancellationToken));
        }

        if (options.Includes(SearchResultTypeDto.Task))
        {
            documents.AddRange(await GetTaskDocumentsAsync(options, cancellationToken));
        }

        if (options.Includes(SearchResultTypeDto.Reminder))
        {
            documents.AddRange(await GetReminderDocumentsAsync(options, cancellationToken));
        }

        if (options.Includes(SearchResultTypeDto.SavedLocation))
        {
            documents.AddRange(await GetSavedLocationDocumentsAsync(options, cancellationToken));
        }

        if (options.Includes(SearchResultTypeDto.MileageEntry))
        {
            documents.AddRange(await GetMileageEntryDocumentsAsync(options, cancellationToken));
        }

        if (options.Includes(SearchResultTypeDto.AssistantInteraction))
        {
            documents.AddRange(await GetAssistantInteractionDocumentsAsync(options, cancellationToken));
        }

        List<SearchMatch> matches = documents
            .Select(document => MatchDocument(document, queryTerms))
            .Where(match => match != null)
            .Select(match => match!)
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Document.RelevantAtUtc ?? match.Document.CreatedAtUtc)
            .ThenBy(match => match.Document.Type)
            .ThenBy(match => match.Document.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<SearchResultDto> pagedResults = matches
            .Skip(options.Skip)
            .Take(options.Take)
            .Select(match => MapToDto(match, queryTerms))
            .ToList();

        return new SearchResponse(
            options.Query,
            options.Types.Count == 0 ? null : options.Types.OrderBy(type => type).ToArray(),
            options.FromUtc,
            options.ToUtc,
            options.Skip,
            options.Take,
            matches.Count,
            pagedResults);
    }

    private async Task<IReadOnlyList<SearchDocument>> GetNoteDocumentsAsync(
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        IQueryable<Note> query = ApplyCreatedAtFilters(
            noahDbContext.Notes.AsNoTracking(),
            options);

        var notes = await query
            .Select(note => new
            {
                note.Id,
                note.Title,
                note.Content,
                note.CapturedFromVoice,
                note.SourceInteractionId,
                note.CreatedAtUtc,
                note.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return notes
            .Select(note => new SearchDocument(
                note.Id,
                SearchResultTypeDto.Note,
                note.Title,
                note.CreatedAtUtc,
                note.UpdatedAtUtc,
                note.CreatedAtUtc,
                new Dictionary<string, string?>
                {
                    ["title"] = note.Title,
                    ["content"] = note.Content,
                    ["capturedFromVoice"] = note.CapturedFromVoice.ToString(CultureInfo.InvariantCulture),
                    ["sourceInteractionId"] = note.SourceInteractionId?.ToString()
                },
                new[]
                {
                    note.Content,
                    note.CapturedFromVoice ? "Captured from voice" : null
                }))
            .ToList();
    }

    private async Task<IReadOnlyList<SearchDocument>> GetTaskDocumentsAsync(
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        IQueryable<TaskItem> query = noahDbContext.TaskItems.AsNoTracking();

        if (options.FromUtc.HasValue)
        {
            DateTimeOffset fromUtc = options.FromUtc.Value;
            query = query.Where(taskItem => (taskItem.DueAtUtc ?? taskItem.CreatedAtUtc) >= fromUtc);
        }

        if (options.ToUtc.HasValue)
        {
            DateTimeOffset toUtc = options.ToUtc.Value;
            query = query.Where(taskItem => (taskItem.DueAtUtc ?? taskItem.CreatedAtUtc) <= toUtc);
        }

        var taskItems = await query
            .Select(taskItem => new
            {
                taskItem.Id,
                taskItem.Title,
                taskItem.Description,
                taskItem.Status,
                taskItem.Priority,
                taskItem.DueAtUtc,
                taskItem.PlannedFor,
                taskItem.CreatedAtUtc,
                taskItem.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return taskItems
            .Select(taskItem => new SearchDocument(
                taskItem.Id,
                SearchResultTypeDto.Task,
                taskItem.Title,
                taskItem.CreatedAtUtc,
                taskItem.UpdatedAtUtc,
                taskItem.DueAtUtc ?? taskItem.CreatedAtUtc,
                new Dictionary<string, string?>
                {
                    ["title"] = taskItem.Title,
                    ["description"] = taskItem.Description,
                    ["status"] = taskItem.Status.ToString(),
                    ["priority"] = taskItem.Priority.ToString(),
                    ["dueAtUtc"] = FormatDateTime(taskItem.DueAtUtc),
                    ["plannedFor"] = FormatDateOnly(taskItem.PlannedFor)
                },
                new[]
                {
                    taskItem.Description,
                    JoinPreviewParts(
                        $"Status: {taskItem.Status}",
                        $"Priority: {taskItem.Priority}",
                        taskItem.DueAtUtc.HasValue ? $"Due: {FormatDateTime(taskItem.DueAtUtc)}" : null,
                        taskItem.PlannedFor.HasValue ? $"Planned for: {FormatDateOnly(taskItem.PlannedFor)}" : null)
                }))
            .ToList();
    }

    private async Task<IReadOnlyList<SearchDocument>> GetReminderDocumentsAsync(
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        IQueryable<Reminder> query = noahDbContext.Reminders.AsNoTracking();

        if (options.FromUtc.HasValue)
        {
            DateTimeOffset fromUtc = options.FromUtc.Value;
            query = query.Where(reminder => (reminder.TriggerAtUtc ?? reminder.CreatedAtUtc) >= fromUtc);
        }

        if (options.ToUtc.HasValue)
        {
            DateTimeOffset toUtc = options.ToUtc.Value;
            query = query.Where(reminder => (reminder.TriggerAtUtc ?? reminder.CreatedAtUtc) <= toUtc);
        }

        var reminders = await query
            .Select(reminder => new
            {
                reminder.Id,
                reminder.Title,
                reminder.Message,
                reminder.TriggerType,
                reminder.Status,
                reminder.ShouldNotify,
                reminder.TriggerAtUtc,
                reminder.TriggerRadiusMeters,
                reminder.LastTriggeredAtUtc,
                reminder.TaskItemId,
                reminder.NoteId,
                reminder.SavedLocationId,
                reminder.CreatedAtUtc,
                reminder.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return reminders
            .Select(reminder => new SearchDocument(
                reminder.Id,
                SearchResultTypeDto.Reminder,
                reminder.Title,
                reminder.CreatedAtUtc,
                reminder.UpdatedAtUtc,
                reminder.TriggerAtUtc ?? reminder.CreatedAtUtc,
                new Dictionary<string, string?>
                {
                    ["title"] = reminder.Title,
                    ["message"] = reminder.Message,
                    ["triggerType"] = reminder.TriggerType.ToString(),
                    ["status"] = reminder.Status.ToString(),
                    ["shouldNotify"] = reminder.ShouldNotify.ToString(CultureInfo.InvariantCulture),
                    ["triggerAtUtc"] = FormatDateTime(reminder.TriggerAtUtc),
                    ["triggerRadiusMeters"] = FormatDouble(reminder.TriggerRadiusMeters),
                    ["lastTriggeredAtUtc"] = FormatDateTime(reminder.LastTriggeredAtUtc),
                    ["taskItemId"] = reminder.TaskItemId?.ToString(),
                    ["noteId"] = reminder.NoteId?.ToString(),
                    ["savedLocationId"] = reminder.SavedLocationId?.ToString()
                },
                new[]
                {
                    reminder.Message,
                    JoinPreviewParts(
                        $"Status: {reminder.Status}",
                        $"Trigger: {reminder.TriggerType}",
                        reminder.TriggerAtUtc.HasValue ? $"At: {FormatDateTime(reminder.TriggerAtUtc)}" : null,
                        reminder.TriggerRadiusMeters.HasValue
                            ? $"Radius: {FormatDouble(reminder.TriggerRadiusMeters)} meters"
                            : null)
                }))
            .ToList();
    }

    private async Task<IReadOnlyList<SearchDocument>> GetSavedLocationDocumentsAsync(
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        IQueryable<SavedLocation> query = ApplyCreatedAtFilters(
            noahDbContext.SavedLocations.AsNoTracking(),
            options);

        var savedLocations = await query
            .Select(savedLocation => new
            {
                savedLocation.Id,
                savedLocation.Name,
                savedLocation.Address,
                savedLocation.CreatedFromCurrentLocation,
                savedLocation.Coordinate.Latitude,
                savedLocation.Coordinate.Longitude,
                savedLocation.Coordinate.AccuracyMeters,
                savedLocation.CreatedAtUtc,
                savedLocation.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return savedLocations
            .Select(savedLocation => new SearchDocument(
                savedLocation.Id,
                SearchResultTypeDto.SavedLocation,
                savedLocation.Name,
                savedLocation.CreatedAtUtc,
                savedLocation.UpdatedAtUtc,
                savedLocation.CreatedAtUtc,
                new Dictionary<string, string?>
                {
                    ["name"] = savedLocation.Name,
                    ["address"] = savedLocation.Address,
                    ["createdFromCurrentLocation"] =
                        savedLocation.CreatedFromCurrentLocation.ToString(CultureInfo.InvariantCulture),
                    ["latitude"] = FormatDouble(savedLocation.Latitude),
                    ["longitude"] = FormatDouble(savedLocation.Longitude),
                    ["accuracyMeters"] = FormatDouble(savedLocation.AccuracyMeters)
                },
                new[]
                {
                    savedLocation.Address,
                    JoinPreviewParts(
                        $"Latitude: {FormatDouble(savedLocation.Latitude)}",
                        $"Longitude: {FormatDouble(savedLocation.Longitude)}",
                        savedLocation.AccuracyMeters.HasValue
                            ? $"Accuracy: {FormatDouble(savedLocation.AccuracyMeters)} meters"
                            : null,
                        savedLocation.CreatedFromCurrentLocation ? "Saved from current location" : null)
                }))
            .ToList();
    }

    private async Task<IReadOnlyList<SearchDocument>> GetMileageEntryDocumentsAsync(
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        IQueryable<MileageEntry> query = noahDbContext.MileageEntries.AsNoTracking();

        if (options.FromUtc.HasValue)
        {
            DateTimeOffset fromUtc = options.FromUtc.Value;
            query = query.Where(mileageEntry => mileageEntry.RecordedAtUtc >= fromUtc);
        }

        if (options.ToUtc.HasValue)
        {
            DateTimeOffset toUtc = options.ToUtc.Value;
            query = query.Where(mileageEntry => mileageEntry.RecordedAtUtc <= toUtc);
        }

        var mileageEntries = await query
            .Select(mileageEntry => new
            {
                mileageEntry.Id,
                mileageEntry.RecordedAtUtc,
                mileageEntry.OdometerReadingKm,
                mileageEntry.TripDistanceKm,
                mileageEntry.Source,
                mileageEntry.SourceImagePath,
                mileageEntry.RecognizedText,
                mileageEntry.CorrectedText,
                mileageEntry.IsTextCorrected,
                mileageEntry.Notes,
                mileageEntry.CreatedAtUtc,
                mileageEntry.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return mileageEntries
            .Select(mileageEntry => new SearchDocument(
                mileageEntry.Id,
                SearchResultTypeDto.MileageEntry,
                $"Mileage entry - {FormatDecimal(mileageEntry.OdometerReadingKm)} km",
                mileageEntry.CreatedAtUtc,
                mileageEntry.UpdatedAtUtc,
                mileageEntry.RecordedAtUtc,
                new Dictionary<string, string?>
                {
                    ["title"] = $"Mileage entry {FormatDecimal(mileageEntry.OdometerReadingKm)} km",
                    ["recordedAtUtc"] = FormatDateTime(mileageEntry.RecordedAtUtc),
                    ["odometerReadingKm"] = FormatDecimal(mileageEntry.OdometerReadingKm),
                    ["tripDistanceKm"] = FormatDecimal(mileageEntry.TripDistanceKm),
                    ["source"] = mileageEntry.Source.ToString(),
                    ["sourceImagePath"] = mileageEntry.SourceImagePath,
                    ["recognizedText"] = mileageEntry.RecognizedText,
                    ["correctedText"] = mileageEntry.CorrectedText,
                    ["isTextCorrected"] = mileageEntry.IsTextCorrected.ToString(CultureInfo.InvariantCulture),
                    ["notes"] = mileageEntry.Notes
                },
                new[]
                {
                    mileageEntry.Notes,
                    mileageEntry.CorrectedText,
                    mileageEntry.RecognizedText,
                    JoinPreviewParts(
                        $"Recorded: {FormatDateTime(mileageEntry.RecordedAtUtc)}",
                        $"Odometer: {FormatDecimal(mileageEntry.OdometerReadingKm)} km",
                        mileageEntry.TripDistanceKm.HasValue
                            ? $"Trip: {FormatDecimal(mileageEntry.TripDistanceKm)} km"
                            : null,
                        $"Source: {mileageEntry.Source}")
                }))
            .ToList();
    }

    private async Task<IReadOnlyList<SearchDocument>> GetAssistantInteractionDocumentsAsync(
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        IQueryable<AssistantInteraction> query = noahDbContext.AssistantInteractions.AsNoTracking();

        if (options.FromUtc.HasValue)
        {
            DateTimeOffset fromUtc = options.FromUtc.Value;
            query = query.Where(interaction => interaction.RequestedAtUtc >= fromUtc);
        }

        if (options.ToUtc.HasValue)
        {
            DateTimeOffset toUtc = options.ToUtc.Value;
            query = query.Where(interaction => interaction.RequestedAtUtc <= toUtc);
        }

        var interactions = await query
            .Select(interaction => new
            {
                interaction.Id,
                interaction.UserInput,
                interaction.InputMode,
                interaction.ActionType,
                interaction.AssistantResponse,
                interaction.ResponseMode,
                interaction.Status,
                interaction.RelatedEntityId,
                interaction.RelatedEntityType,
                interaction.ErrorMessage,
                interaction.RequestedAtUtc,
                interaction.CompletedAtUtc,
                interaction.CreatedAtUtc,
                interaction.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return interactions
            .Select(interaction => new SearchDocument(
                interaction.Id,
                SearchResultTypeDto.AssistantInteraction,
                CreateTitleFromText(interaction.UserInput, "Assistant interaction"),
                interaction.CreatedAtUtc,
                interaction.UpdatedAtUtc,
                interaction.RequestedAtUtc,
                new Dictionary<string, string?>
                {
                    ["userInput"] = interaction.UserInput,
                    ["inputMode"] = interaction.InputMode.ToString(),
                    ["actionType"] = interaction.ActionType.ToString(),
                    ["assistantResponse"] = interaction.AssistantResponse,
                    ["responseMode"] = interaction.ResponseMode.ToString(),
                    ["status"] = interaction.Status.ToString(),
                    ["relatedEntityId"] = interaction.RelatedEntityId?.ToString(),
                    ["relatedEntityType"] = interaction.RelatedEntityType,
                    ["errorMessage"] = interaction.ErrorMessage,
                    ["requestedAtUtc"] = FormatDateTime(interaction.RequestedAtUtc),
                    ["completedAtUtc"] = FormatDateTime(interaction.CompletedAtUtc)
                },
                new[]
                {
                    interaction.AssistantResponse,
                    interaction.ErrorMessage,
                    JoinPreviewParts(
                        $"Status: {interaction.Status}",
                        $"Action: {interaction.ActionType}",
                        $"Requested: {FormatDateTime(interaction.RequestedAtUtc)}",
                        interaction.RelatedEntityType != null
                            ? $"Related: {interaction.RelatedEntityType}"
                            : null)
                }))
            .ToList();
    }

    private static IQueryable<TEntity> ApplyCreatedAtFilters<TEntity>(
        IQueryable<TEntity> query,
        SearchOptions options)
        where TEntity : TrackedEntity
    {
        if (options.FromUtc.HasValue)
        {
            DateTimeOffset fromUtc = options.FromUtc.Value;
            query = query.Where(entity => entity.CreatedAtUtc >= fromUtc);
        }

        if (options.ToUtc.HasValue)
        {
            DateTimeOffset toUtc = options.ToUtc.Value;
            query = query.Where(entity => entity.CreatedAtUtc <= toUtc);
        }

        return query;
    }

    private static SearchMatch? MatchDocument(SearchDocument document, IReadOnlyList<string> queryTerms)
    {
        if (queryTerms.Count == 0)
        {
            return new SearchMatch(document, 0, Array.Empty<string>());
        }

        HashSet<string> matchedFields = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> matchedTerms = new(StringComparer.OrdinalIgnoreCase);
        int score = 0;

        foreach (KeyValuePair<string, string?> field in document.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Value))
            {
                continue;
            }

            foreach (string queryTerm in queryTerms)
            {
                if (!field.Value.Contains(queryTerm, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matchedFields.Add(field.Key);
                matchedTerms.Add(queryTerm);
                score += ScoreFieldMatch(field.Key, field.Value, queryTerm);
            }
        }

        if (matchedTerms.Count != queryTerms.Count)
        {
            return null;
        }

        return new SearchMatch(
            document,
            score,
            matchedFields.OrderBy(fieldName => fieldName, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static SearchResultDto MapToDto(SearchMatch match, IReadOnlyList<string> queryTerms)
    {
        return new SearchResultDto(
            match.Document.Id,
            match.Document.Type,
            match.Document.Title,
            CreatePreview(match.Document.PreviewCandidates, queryTerms),
            match.Document.CreatedAtUtc,
            match.Document.UpdatedAtUtc,
            match.Document.RelevantAtUtc,
            match.MatchedFields);
    }

    private static int ScoreFieldMatch(string fieldName, string value, string queryTerm)
    {
        int score = fieldName is "title" or "name" or "userInput"
            ? 40
            : 10;

        if (string.Equals(value, queryTerm, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }
        else if (value.StartsWith(queryTerm, StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        return score;
    }

    private static string[] SplitQuery(string query)
    {
        return query
            .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? CreatePreview(IReadOnlyList<string?> previewCandidates, IReadOnlyList<string> queryTerms)
    {
        foreach (string previewCandidate in previewCandidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate))!)
        {
            if (queryTerms.Count == 0)
            {
                return Truncate(NormalizeWhitespace(previewCandidate), MaximumPreviewLength);
            }

            string? matchingSnippet = CreateMatchingSnippet(previewCandidate, queryTerms);

            if (matchingSnippet != null)
            {
                return matchingSnippet;
            }
        }

        string? fallbackPreview = previewCandidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

        return fallbackPreview == null
            ? null
            : Truncate(NormalizeWhitespace(fallbackPreview), MaximumPreviewLength);
    }

    private static string? CreateMatchingSnippet(string value, IReadOnlyList<string> queryTerms)
    {
        string normalizedValue = NormalizeWhitespace(value);
        int matchIndex = -1;

        foreach (string queryTerm in queryTerms)
        {
            matchIndex = normalizedValue.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase);

            if (matchIndex >= 0)
            {
                break;
            }
        }

        if (matchIndex < 0)
        {
            return null;
        }

        int startIndex = Math.Max(0, matchIndex - 40);
        int length = Math.Min(MaximumPreviewLength, normalizedValue.Length - startIndex);
        string snippet = normalizedValue.Substring(startIndex, length);

        if (startIndex > 0)
        {
            snippet = "..." + snippet;
        }

        if (startIndex + length < normalizedValue.Length)
        {
            snippet += "...";
        }

        return snippet;
    }

    private static string CreateTitleFromText(string value, string fallbackTitle)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallbackTitle;
        }

        return Truncate(NormalizeWhitespace(value), 80);
    }

    private static string? JoinPreviewParts(params string?[] parts)
    {
        string[] populatedParts = parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!)
            .ToArray();

        return populatedParts.Length == 0
            ? null
            : string.Join(" | ", populatedParts);
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(
            " ",
            value.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Truncate(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        return value[..maximumLength] + "...";
    }

    private static string FormatDateTime(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture);
    }

    private static string? FormatDateTime(DateTimeOffset? value)
    {
        return value.HasValue
            ? FormatDateTime(value.Value)
            : null;
    }

    private static string? FormatDateOnly(DateOnly? value)
    {
        return value.HasValue
            ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string? FormatDecimal(decimal? value)
    {
        return value.HasValue
            ? FormatDecimal(value.Value)
            : null;
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string? FormatDouble(double? value)
    {
        return value.HasValue
            ? FormatDouble(value.Value)
            : null;
    }

    private sealed record SearchOptions(
        string Query,
        HashSet<SearchResultTypeDto> Types,
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        int Skip,
        int Take)
    {
        public bool Includes(SearchResultTypeDto type)
        {
            return Types.Count == 0 || Types.Contains(type);
        }

        public static SearchOptions FromRequest(SearchRequest request)
        {
            HashSet<SearchResultTypeDto> types = request.Types?
                .Where(type => Enum.IsDefined(type))
                .ToHashSet() ?? new HashSet<SearchResultTypeDto>();

            int take = request.Take <= 0
                ? DefaultTake
                : Math.Min(request.Take, MaximumTake);

            return new SearchOptions(
                request.Query?.Trim() ?? string.Empty,
                types,
                request.FromUtc?.ToUniversalTime(),
                request.ToUtc?.ToUniversalTime(),
                Math.Max(0, request.Skip),
                take);
        }
    }

    private sealed record SearchDocument(
        Guid Id,
        SearchResultTypeDto Type,
        string Title,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        DateTimeOffset? RelevantAtUtc,
        IReadOnlyDictionary<string, string?> Fields,
        IReadOnlyList<string?> PreviewCandidates);

    private sealed record SearchMatch(
        SearchDocument Document,
        int Score,
        IReadOnlyList<string> MatchedFields);
}
