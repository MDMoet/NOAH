using NOAH.Contracts.Common;

namespace Application.Models;

/// <summary>
/// Contains contextual information used to build the LLM prompt.
/// </summary>
/// <param name="CurrentDateTimeUtc">The current UTC timestamp when the prompt is built.</param>
/// <param name="CurrentLocation">The user's current location when supplied by the client.</param>
/// <param name="SearchResults">Relevant NOAH search results for the user's message.</param>
public sealed record AssistantPromptContext(
    DateTimeOffset CurrentDateTimeUtc,
    GeoCoordinateDto? CurrentLocation,
    IReadOnlyList<AssistantContextSearchResult> SearchResults);
