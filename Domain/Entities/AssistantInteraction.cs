using NOAH.Domain.Common;
using NOAH.Domain.Enums;

namespace NOAH.Domain.Entities;

public sealed class AssistantInteraction : TrackedEntity
{
    public string UserInput { get; set; } = string.Empty;

    public AssistantInputMode InputMode { get; set; }

    public AssistantActionType ActionType { get; set; } = AssistantActionType.Unknown;

    public string? AssistantResponse { get; set; }

    public AssistantResponseMode ResponseMode { get; set; } = AssistantResponseMode.Text;

    public AssistantInteractionStatus Status { get; set; } = AssistantInteractionStatus.Received;

    public Guid? RelatedEntityId { get; set; }

    public string? RelatedEntityType { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }
}
