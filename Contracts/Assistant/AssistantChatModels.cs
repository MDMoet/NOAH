namespace NOAH.Contracts.Assistant;

public sealed record AssistantChatDto(
    Guid Id,
    string Title,
    string? Description,
    bool IsArchived,
    int MessageCount,
    string? LastMessagePreview,
    DateTimeOffset? LastMessageAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateAssistantChatRequest(
    string? Title,
    string? Description);

public sealed record UpdateAssistantChatRequest(
    string? Title,
    string? Description,
    bool? IsArchived);
