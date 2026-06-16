using NOAH.Contracts.Assistant;
using NOAH.Contracts.Common;
using NOAH.Contracts.Enums;

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

/// <summary>
/// Represents one recent completed assistant exchange shared across routed models.
/// </summary>
public sealed record AssistantConversationMemoryEntry(
    Guid InteractionId,
    string UserInput,
    string AssistantResponse,
    AssistantActionTypeDto ActionType,
    DateTimeOffset RequestedAtUtc);

/// <summary>
/// Contains contextual information used to build the assistant prompt.
/// </summary>
public sealed record AssistantPromptContext
{
    /// <summary>
    /// The current UTC timestamp when the prompt is built.
    /// </summary>
    public DateTimeOffset CurrentDateTimeUtc { get; init; }

    /// <summary>
    /// The user's current location when supplied by the client.
    /// </summary>
    public GeoCoordinateDto? CurrentLocation { get; init; }

    /// <summary>
    /// Relevant NOAH search results for the user's message.
    /// </summary>
    public IReadOnlyList<AssistantContextSearchResult> SearchResults { get; init; } =
        Array.Empty<AssistantContextSearchResult>();

    /// <summary>
    /// Recent completed assistant exchanges shared across models.
    /// </summary>
    public IReadOnlyList<AssistantConversationMemoryEntry> ConversationMemory { get; init; } =
        Array.Empty<AssistantConversationMemoryEntry>();
}

/// <summary>
/// Request used when the assistant attempts to execute a concrete NOAH action.
/// </summary>
/// <param name="Command">The original assistant command.</param>
/// <param name="InteractionId">The persisted assistant interaction id for audit linking.</param>
public sealed record AssistantToolActionRequest(
    AssistantCommandRequest Command,
    Guid InteractionId);

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

/// <summary>
/// Represents a structured assistant tool plan proposed by the LLM.
/// </summary>
public sealed record AssistantPlannedToolAction(
    AssistantActionTypeDto ActionType,
    string? Title,
    string? Description,
    string? Query,
    string? ScheduledAt,
    string? EndsAt,
    string? TimeZoneId,
    TaskPriorityDto? Priority,
    bool CreateLinkedReminder,
    string? ReminderAt,
    string? ReminderTitle,
    string? ReminderMessage,
    string? ResponseText);

/// <summary>
/// Wraps a structured assistant tool plan together with request context.
/// </summary>
public sealed record AssistantPlannedToolActionRequest(
    AssistantCommandRequest Command,
    Guid InteractionId,
    AssistantPlannedToolAction Action);

/// <summary>
/// Describes the selected model, fallback order, and prompt role for one assistant request.
/// </summary>
public sealed record AssistantModelRoutingDecision(
    string PrimaryModelKey,
    IReadOnlyList<string> FallbackModelKeys,
    string SystemPrompt,
    string Reason,
    bool UsesCodingModel);

/// <summary>
/// Describes the current activity state of one logical model session.
/// </summary>
public sealed record AssistantModelSessionStatus(
    string ModelKey,
    bool IsActive,
    DateTimeOffset? LastActivityAtUtc,
    string Reason);
