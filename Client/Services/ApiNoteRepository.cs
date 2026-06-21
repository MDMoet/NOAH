using Client.Models;
using NOAH.Contracts.Notes;

namespace Client.Services;

public sealed class ApiNoteRepository(NoahApiClient apiClient) : INoteRepository
{
    public async Task<IReadOnlyList<Note>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NoteDto> notes = await apiClient.GetAsync<IReadOnlyList<NoteDto>>("notes", cancellationToken);

        return notes
            .OrderByDescending(note => note.UpdatedAtUtc ?? note.CreatedAtUtc)
            .Select(Map)
            .ToList();
    }

    public async Task<IReadOnlyList<Note>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Note> notes = await GetAllAsync(cancellationToken);

        return notes
            .Take(Math.Max(1, count))
            .ToList();
    }

    public async Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        try
        {
            NoteDto note = await apiClient.GetAsync<NoteDto>($"notes/{id:D}", cancellationToken);
            return Map(note);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Note>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NoteDto> notes = await apiClient.GetAsync<IReadOnlyList<NoteDto>>("notes", cancellationToken);

        return notes
            .Where(note =>
                note.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                note.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(note => note.UpdatedAtUtc ?? note.CreatedAtUtc)
            .Select(Map)
            .ToList();
    }

    public async Task<Note> SaveAsync(Note note, CancellationToken cancellationToken = default)
    {
        if (note.Id == Guid.Empty)
        {
            CreateNoteRequest request = new(
                note.Title.Trim(),
                note.Content.Trim(),
                note.CapturedFromVoice,
                note.SourceInteractionId);

            NoteDto created = await apiClient.PostAsync<CreateNoteRequest, NoteDto>("notes", request, cancellationToken);
            return Map(created);
        }

        UpdateNoteRequest updateRequest = new(
            note.Title.Trim(),
            note.Content.Trim());

        NoteDto updated = await apiClient.PutAsync<UpdateNoteRequest, NoteDto>($"notes/{note.Id:D}", updateRequest, cancellationToken);
        return Map(updated);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => apiClient.DeleteAsync($"notes/{id:D}", cancellationToken);

    private static Note Map(NoteDto noteDto)
    {
        return new Note
        {
            Id = noteDto.Id,
            Title = noteDto.Title,
            Content = noteDto.Content,
            CapturedFromVoice = noteDto.CapturedFromVoice,
            SourceInteractionId = noteDto.SourceInteractionId,
            CreatedAtUtc = noteDto.CreatedAtUtc,
            UpdatedAtUtc = noteDto.UpdatedAtUtc
        };
    }
}
