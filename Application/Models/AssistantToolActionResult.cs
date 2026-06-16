using NOAH.Contracts.Enums;

namespace Application.Models;

/// <summary>
/// Result returned after attempting to execute an assistant tool action.
/// </summary>
/// <param name="WasHandled">Whether a concrete tool handled the user request.</param>
/// <param name="ActionType">The assistant action type that was executed.</param>
/// <param name="ResponseText">The response text to return to the user.</param>
/// <param name="RelatedEntityId">The id of an entity created or affected by the action.</param>
/// <param name="RelatedEntityType">The type of entity created or affected by the action.</param>
/// <param name="SearchResults">Search results produced by the action, when relevant.</param>
public sealed record AssistantToolActionResult(
    bool WasHandled,
    AssistantActionTypeDto ActionType,
    string? ResponseText,
    Guid? RelatedEntityId,
    string? RelatedEntityType,
    IReadOnlyList<AssistantContextSearchResult> SearchResults)
{
    /// <summary>
    /// A reusable result for messages that should continue to LLM processing.
    /// </summary>
    public static AssistantToolActionResult NotHandled { get; } = new(
        false,
        AssistantActionTypeDto.Unknown,
        null,
        null,
        null,
        Array.Empty<AssistantContextSearchResult>());
}
