using NOAH.Domain.Common;

namespace NOAH.Domain.Entities;

public sealed class Note : TrackedEntity
{
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public bool CapturedFromVoice { get; set; }

    public Guid? SourceInteractionId { get; set; }
}
