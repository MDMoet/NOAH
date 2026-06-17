namespace NOAH.Contracts.Assistant;

public sealed record AssistantMemoryItemDto(
    Guid Id,
    string Title,
    string Content,
    string? Tags,
    bool IsPinned,
    Guid? SourceInteractionId,
    Guid? SourceChatId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateAssistantMemoryItemRequest(
    string Title,
    string Content,
    string? Tags,
    bool IsPinned,
    Guid? SourceInteractionId,
    Guid? SourceChatId);

public sealed record UpdateAssistantMemoryItemRequest(
    string Title,
    string Content,
    string? Tags,
    bool IsPinned);
