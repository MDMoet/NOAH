using NOAH.Contracts.Enums;

namespace NOAH.Contracts.Assistant;

public sealed record AssistantSettingsDto(
    Guid Id,
    AssistantResponseModeDto PreferredResponseMode,
    string SpeechCulture);

public sealed record UpdateAssistantSettingsRequest(
    AssistantResponseModeDto PreferredResponseMode,
    string SpeechCulture);
