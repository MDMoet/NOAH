using NOAH.Domain.Common;

namespace NOAH.Domain.Entities;

public sealed class AssistantMemoryItem : TrackedEntity
{
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? Tags { get; set; }

    public bool IsPinned { get; set; }

    public Guid? SourceInteractionId { get; set; }

    public Guid? SourceChatId { get; set; }

    public AssistantInteraction? SourceInteraction { get; set; }

    public AssistantChat? SourceChat { get; set; }
}
