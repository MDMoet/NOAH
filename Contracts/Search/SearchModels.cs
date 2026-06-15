using NOAH.Contracts.Enums;

namespace NOAH.Contracts.Search;

/// <summary>
/// Request used to search across NOAH entities.
/// </summary>
/// <param name="Query">The optional search text. When omitted, the newest matching entities are returned.</param>
/// <param name="Types">The optional entity types to include.</param>
/// <param name="FromUtc">The optional inclusive lower relevant-date boundary.</param>
/// <param name="ToUtc">The optional inclusive upper relevant-date boundary.</param>
/// <param name="Skip">The number of matching results to skip.</param>
/// <param name="Take">The maximum number of matching results to return.</param>
public sealed record SearchRequest(
    string? Query,
    IReadOnlyList<SearchResultTypeDto>? Types,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Skip,
    int Take);

/// <summary>
/// Represents one result from a cross-entity search.
/// </summary>
/// <param name="Id">The unique identifier of the matched entity.</param>
/// <param name="Type">The type of entity that matched.</param>
/// <param name="Title">The primary display title for the result.</param>
/// <param name="Preview">A short preview of the matched content.</param>
/// <param name="CreatedAtUtc">The UTC timestamp when the entity was created.</param>
/// <param name="UpdatedAtUtc">The UTC timestamp when the entity was last updated.</param>
/// <param name="RelevantAtUtc">The UTC timestamp most relevant for the entity, such as due date or recorded date.</param>
/// <param name="MatchedFields">The fields that matched the search text.</param>
public sealed record SearchResultDto(
    Guid Id,
    SearchResultTypeDto Type,
    string Title,
    string? Preview,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? RelevantAtUtc,
    IReadOnlyList<string> MatchedFields);

/// <summary>
/// Response containing cross-entity search results.
/// </summary>
/// <param name="Query">The normalized query text used for the search.</param>
/// <param name="Types">The entity types included in the search.</param>
/// <param name="FromUtc">The inclusive lower relevant-date boundary used for the search.</param>
/// <param name="ToUtc">The inclusive upper relevant-date boundary used for the search.</param>
/// <param name="Skip">The number of matching results skipped.</param>
/// <param name="Take">The maximum number of matching results requested.</param>
/// <param name="TotalCount">The total number of matching results before paging.</param>
/// <param name="Results">The paged search results.</param>
public sealed record SearchResponse(
    string Query,
    IReadOnlyList<SearchResultTypeDto>? Types,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Skip,
    int Take,
    int TotalCount,
    IReadOnlyList<SearchResultDto> Results);
