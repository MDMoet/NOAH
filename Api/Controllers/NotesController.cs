using Api.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NOAH.Contracts.Notes;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class NotesController(INotesService notesService, ILogger<NotesController> logger)
    : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<NoteDto>> CreateNoteAsync(
        [FromBody] CreateNoteRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateCreateNoteRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        NoteDto createdNote = await notesService.CreateNoteAsync(request!, cancellationToken);

        logger.LogInformation("Created note with id {NoteId}.", createdNote.Id);

        return CreatedAtAction(
            "GetNoteById",
            new { noteId = createdNote.Id },
            createdNote);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NoteDto>>> GetNotesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NoteDto> notes = await notesService.GetNotesAsync(cancellationToken);

        return Ok(notes);
    }

    [HttpGet("{noteId:guid}", Name = "GetNoteById")]
    public async Task<ActionResult<NoteDto>> GetNoteByIdAsync(
        Guid noteId,
        CancellationToken cancellationToken)
    {
        if (noteId == Guid.Empty)
        {
            return BadRequest("Note id cannot be empty.");
        }

        NoteDto? note = await notesService.GetNoteByIdAsync(noteId, cancellationToken);

        if (note == null)
        {
            return NotFound();
        }

        return Ok(note);
    }

    [HttpPut("{noteId:guid}")]
    public async Task<ActionResult<NoteDto>> UpdateNoteAsync(
        Guid noteId,
        [FromBody] UpdateNoteRequest? request,
        CancellationToken cancellationToken)
    {
        if (noteId == Guid.Empty)
        {
            return BadRequest("Note id cannot be empty.");
        }

        Dictionary<string, string[]> validationErrors = ValidateUpdateNoteRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        NoteDto? updatedNote = await notesService.UpdateNoteAsync(
            noteId,
            request!,
            cancellationToken);

        if (updatedNote == null)
        {
            return NotFound();
        }

        logger.LogInformation("Updated note with id {NoteId}.", noteId);

        return Ok(updatedNote);
    }

    [HttpPatch("{noteId:guid}")]
    public async Task<ActionResult<NoteDto>> PartialUpdateNoteAsync(
        Guid noteId,
        [FromBody] PartialUpdateNoteRequest? request,
        CancellationToken cancellationToken)
    {
        if (noteId == Guid.Empty)
        {
            return BadRequest("Note id cannot be empty.");
        }

        Dictionary<string, string[]> validationErrors = ValidatePartialUpdateNoteRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        NoteDto? updatedNote = await notesService.PartialUpdateNoteAsync(
            noteId,
            request!,
            cancellationToken);

        if (updatedNote == null)
        {
            return NotFound();
        }

        logger.LogInformation("Partially updated note with id {NoteId}.", noteId);

        return Ok(updatedNote);
    }

    [HttpDelete("{noteId:guid}")]
    public async Task<IActionResult> DeleteNoteAsync(
        Guid noteId,
        CancellationToken cancellationToken)
    {
        if (noteId == Guid.Empty)
        {
            return BadRequest("Note id cannot be empty.");
        }

        bool noteWasDeleted = await notesService.DeleteNoteAsync(noteId, cancellationToken);

        if (!noteWasDeleted)
        {
            return NotFound();
        }

        logger.LogInformation("Deleted note with id {NoteId}.", noteId);

        return NoContent();
    }

    private static Dictionary<string, string[]> ValidateCreateNoteRequest(CreateNoteRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            validationErrors[nameof(request.Title)] = ["Title is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            validationErrors[nameof(request.Content)] = ["Content is required."];
        }

        if (request.SourceInteractionId == Guid.Empty)
        {
            validationErrors[nameof(request.SourceInteractionId)] = ["Source interaction id cannot be empty."];
        }

        return validationErrors;
    }

    private static Dictionary<string, string[]> ValidateUpdateNoteRequest(UpdateNoteRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            validationErrors[nameof(request.Title)] = ["Title is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            validationErrors[nameof(request.Content)] = ["Content is required."];
        }

        return validationErrors;
    }

    private static Dictionary<string, string[]> ValidatePartialUpdateNoteRequest(PartialUpdateNoteRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        bool titleWasProvided = request.Title != null;
        bool contentWasProvided = request.Content != null;

        if (!titleWasProvided && !contentWasProvided)
        {
            validationErrors["Request"] = ["At least one field must be provided."];
        }

        if (titleWasProvided && string.IsNullOrWhiteSpace(request.Title))
        {
            validationErrors[nameof(request.Title)] = ["Title cannot be empty."];
        }

        if (contentWasProvided && string.IsNullOrWhiteSpace(request.Content))
        {
            validationErrors[nameof(request.Content)] = ["Content cannot be empty."];
        }

        return validationErrors;
    }

    private static ModelStateDictionary ToModelStateDictionary(
        Dictionary<string, string[]> validationErrors)
    {
        ModelStateDictionary modelStateDictionary = new();

        foreach (KeyValuePair<string, string[]> validationError in validationErrors)
        {
            foreach (string errorMessage in validationError.Value)
            {
                modelStateDictionary.AddModelError(validationError.Key, errorMessage);
            }
        }

        return modelStateDictionary;
    }
}
