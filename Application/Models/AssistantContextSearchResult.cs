namespace Application.Models;

/// <summary>
/// Represents a compact search result included in assistant context.
/// </summary>
/// <param name="Id">The unique identifier of the matched entity.</param>
/// <param name="Type">The type of entity that matched.</param>
/// <param name="Title">The display title of the matched entity.</param>
/// <param name="Preview">A short preview of the matched content.</param>
/// <param name="RelevantAtUtc">The most relevant timestamp for the matched entity.</param>
public sealed record AssistantContextSearchResult(
    Guid Id,
    string Type,
    string Title,
    string? Preview,
    DateTimeOffset? RelevantAtUtc);
