using NOAH.Contracts.Assistant;

namespace Application.Models;

/// <summary>
/// Request used when the assistant attempts to execute a concrete NOAH action.
/// </summary>
/// <param name="Command">The original assistant command.</param>
/// <param name="InteractionId">The persisted assistant interaction id for audit linking.</param>
public sealed record AssistantToolActionRequest(
    AssistantCommandRequest Command,
    Guid InteractionId);
