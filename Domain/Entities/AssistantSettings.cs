using NOAH.Domain.Common;
using NOAH.Domain.Enums;

namespace NOAH.Domain.Entities;

public sealed class AssistantSettings : TrackedEntity
{
    public AssistantResponseMode PreferredResponseMode { get; set; } = AssistantResponseMode.Text;

    public string SpeechCulture { get; set; } = "en-US";

    public bool EnableChatMemory { get; set; } = true;

    public bool EnableLongTermMemory { get; set; } = true;

    public bool EnableMemoryCapture { get; set; } = true;

    public int ConversationMemoryMessageCount { get; set; } = 6;

    public int LongTermMemoryItemCount { get; set; } = 6;
}
