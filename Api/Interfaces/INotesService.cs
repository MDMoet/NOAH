using NOAH.Contracts.Notes;

namespace Api.Interfaces;

public interface INotesService
{
    /// <summary>
    /// Gets all notes that are currently available.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A read-only list of notes.</returns>
    Task<IReadOnlyList<NoteDto>> GetNotesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a note by its unique identifier.
    /// </summary>
    /// <param name="noteId">The unique identifier of the note.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The note when found; otherwise, null.</returns>
    Task<NoteDto?> GetNoteByIdAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new note using the provided note details.
    /// </summary>
    /// <param name="request">The note details used to create the note.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The created note.</returns>
    Task<NoteDto> CreateNoteAsync(CreateNoteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully updates an existing note by replacing its editable fields.
    /// </summary>
    /// <param name="noteId">The unique identifier of the note to update.</param>
    /// <param name="request">The new note details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated note when found; otherwise, null.</returns>
    Task<NoteDto?> UpdateNoteAsync(Guid noteId, UpdateNoteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Partially updates an existing note using only the provided fields.
    /// </summary>
    /// <param name="noteId">The unique identifier of the note to update.</param>
    /// <param name="request">The note fields that should be updated.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated note when found; otherwise, null.</returns>
    Task<NoteDto?> PartialUpdateNoteAsync(Guid noteId, PartialUpdateNoteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an existing note by its unique identifier.
    /// </summary>
    /// <param name="noteId">The unique identifier of the note to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True when the note was deleted; otherwise, false.</returns>
    Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken = default);
}