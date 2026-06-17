using NOAH.Domain.Common;

namespace NOAH.Domain.Entities;

public sealed class AssistantChat : TrackedEntity
{
    public string Title { get; set; } = "New chat";

    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    public string? LastMessagePreview { get; set; }

    public DateTimeOffset? LastMessageAtUtc { get; set; }

    public ICollection<AssistantInteraction> Interactions { get; set; } = [];

    public ICollection<AssistantMemoryItem> MemoryItems { get; set; } = [];
}
