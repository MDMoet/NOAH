using NOAH.Domain.Common;
using NOAH.Domain.Enums;

namespace NOAH.Domain.Entities;

public sealed class AssistantSettings : TrackedEntity
{
    public AssistantResponseMode PreferredResponseMode { get; set; } = AssistantResponseMode.Text;

    public string SpeechCulture { get; set; } = "en-US";
}
