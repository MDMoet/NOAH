// ReSharper disable once CheckNamespace
namespace NOAH.Contracts.Notes;

public sealed record NoteDto(
    Guid Id,
    string Title,
    string Content,
    bool CapturedFromVoice,
    Guid? SourceInteractionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateNoteRequest(
    string Title,
    string Content,
    bool CapturedFromVoice,
    Guid? SourceInteractionId);

public sealed record UpdateNoteRequest(
    string Title,
    string Content);

public sealed record PartialUpdateNoteRequest(
    string? Title,
    string? Content);
