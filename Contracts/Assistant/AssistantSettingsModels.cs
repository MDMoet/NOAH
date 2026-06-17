using NOAH.Contracts.Enums;

namespace NOAH.Contracts.Assistant;

public sealed record AssistantSettingsDto(
    Guid Id,
    AssistantResponseModeDto PreferredResponseMode,
    string SpeechCulture,
    bool EnableChatMemory,
    bool EnableLongTermMemory,
    bool EnableMemoryCapture,
    int ConversationMemoryMessageCount,
    int LongTermMemoryItemCount);

public sealed record UpdateAssistantSettingsRequest(
    AssistantResponseModeDto PreferredResponseMode,
    string SpeechCulture,
    bool EnableChatMemory,
    bool EnableLongTermMemory,
    bool EnableMemoryCapture,
    int ConversationMemoryMessageCount,
    int LongTermMemoryItemCount);
