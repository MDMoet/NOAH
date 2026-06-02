using Api.Interfaces;
using Microsoft.EntityFrameworkCore;
using NOAH.Contracts.Notes;
using NOAH.Domain.Entities;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

public sealed class NotesService(NoahDbContext noahDbContext) : INotesService
{
    public async Task<IReadOnlyList<NoteDto>> GetNotesAsync(CancellationToken cancellationToken = default)
    {
        List<NoteDto> notes = await noahDbContext.Notes
            .AsNoTracking()
            .OrderByDescending(note => note.CreatedAtUtc)
            .Select(note => new NoteDto(
                note.Id,
                note.Title,
                note.Content,
                note.CapturedFromVoice,
                note.SourceInteractionId,
                note.CreatedAtUtc,
                note.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return notes;
    }

    public async Task<NoteDto?> GetNoteByIdAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        NoteDto? note = await noahDbContext.Notes
            .AsNoTracking()
            .Where(note => note.Id == noteId)
            .Select(note => new NoteDto(
                note.Id,
                note.Title,
                note.Content,
                note.CapturedFromVoice,
                note.SourceInteractionId,
                note.CreatedAtUtc,
                note.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return note;
    }

    public async Task<NoteDto> CreateNoteAsync(CreateNoteRequest request, CancellationToken cancellationToken = default)
    {
        DateTimeOffset currentDateTimeUtc = DateTimeOffset.UtcNow;

        Note note = new()
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            CapturedFromVoice = request.CapturedFromVoice,
            SourceInteractionId = request.SourceInteractionId,
            CreatedAtUtc = currentDateTimeUtc,
            UpdatedAtUtc = null
        };

        noahDbContext.Notes.Add(note);

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(note);
    }

    public async Task<NoteDto?> UpdateNoteAsync(
        Guid noteId,
        UpdateNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        Note? note = await noahDbContext.Notes
            .FirstOrDefaultAsync(note => note.Id == noteId, cancellationToken);

        if (note == null)
        {
            return null;
        }

        note.Title = request.Title.Trim();
        note.Content = request.Content.Trim();
        note.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(note);
    }

    public async Task<NoteDto?> PartialUpdateNoteAsync(
        Guid noteId,
        PartialUpdateNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        Note? note = await noahDbContext.Notes
            .FirstOrDefaultAsync(note => note.Id == noteId, cancellationToken);

        if (note == null)
        {
            return null;
        }

        if (request.Title != null)
        {
            note.Title = request.Title.Trim();
        }

        if (request.Content != null)
        {
            note.Content = request.Content.Trim();
        }

        note.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(note);
    }

    public async Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        Note? note = await noahDbContext.Notes
            .FirstOrDefaultAsync(note => note.Id == noteId, cancellationToken);

        if (note == null)
        {
            return false;
        }

        noahDbContext.Notes.Remove(note);

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static NoteDto MapToDto(Note note)
    {
        return new NoteDto(
            note.Id,
            note.Title,
            note.Content,
            note.CapturedFromVoice,
            note.SourceInteractionId,
            note.CreatedAtUtc,
            note.UpdatedAtUtc);
    }
}