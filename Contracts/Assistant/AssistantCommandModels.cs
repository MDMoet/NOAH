using NOAH.Contracts.Common;
using NOAH.Contracts.Enums;

namespace NOAH.Contracts.Assistant;

public sealed record AssistantCommandRequest(
    string Input,
    AssistantInputModeDto InputMode,
    AssistantResponseModeDto? PreferredResponseMode,
    GeoCoordinateDto? CurrentLocation,
    DateTimeOffset RequestedAtUtc,
    Guid? ChatId);

public sealed record AssistantCommandResponse(
    Guid InteractionId,
    Guid? ChatId,
    AssistantActionTypeDto ActionType,
    AssistantInteractionStatusDto Status,
    string ResponseText,
    AssistantResponseModeDto ResponseMode,
    Guid? RelatedEntityId,
    string? RelatedEntityType);

public sealed record AssistantInteractionDto(
    Guid Id,
    Guid? ChatId,
    string UserInput,
    AssistantInputModeDto InputMode,
    AssistantActionTypeDto ActionType,
    string? AssistantResponse,
    AssistantResponseModeDto ResponseMode,
    AssistantInteractionStatusDto Status,
    Guid? RelatedEntityId,
    string? RelatedEntityType,
    string? ErrorMessage,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record SpeechTranscriptionRequest(
    string AudioContentBase64,
    string MimeType,
    string Culture);

public sealed record SpeechTranscriptionResponse(
    string Text,
    string Culture,
    double? Confidence);
