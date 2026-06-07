using Api.Helpers;
using Api.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NOAH.Contracts.Common;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Reminders;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class RemindersController(IRemindersService remindersService, ILogger<RemindersController> logger)
    : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ReminderDto>> CreateReminderAsync(
        [FromBody] CreateReminderRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateCreateReminderRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        try
        {
            ReminderDto createdReminder = await remindersService.CreateReminderAsync(request!, cancellationToken);

            logger.LogInformation("Created reminder with id {ReminderId}.", createdReminder.Id);

            return CreatedAtAction(
                "GetReminderById",
                new { reminderId = createdReminder.Id },
                createdReminder);
        }
        catch (ReminderReferenceNotFoundException exception)
        {
            return ReferenceValidationProblem(exception);
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReminderDto>>> GetRemindersAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReminderDto> reminders = await remindersService.GetRemindersAsync(cancellationToken);

        return Ok(reminders);
    }

    [HttpGet("{reminderId:guid}", Name = "GetReminderById")]
    public async Task<ActionResult<ReminderDto>> GetReminderByIdAsync(
        Guid reminderId,
        CancellationToken cancellationToken)
    {
        if (reminderId == Guid.Empty)
        {
            return BadRequest("Reminder id cannot be empty.");
        }

        ReminderDto? reminder = await remindersService.GetReminderByIdAsync(reminderId, cancellationToken);

        if (reminder == null)
        {
            return NotFound();
        }

        return Ok(reminder);
    }

    [HttpPut("{reminderId:guid}")]
    public async Task<ActionResult<ReminderDto>> UpdateReminderAsync(
        Guid reminderId,
        [FromBody] UpdateReminderRequest? request,
        CancellationToken cancellationToken)
    {
        if (reminderId == Guid.Empty)
        {
            return BadRequest("Reminder id cannot be empty.");
        }

        Dictionary<string, string[]> validationErrors = ValidateUpdateReminderRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        try
        {
            ReminderDto? updatedReminder = await remindersService.UpdateReminderAsync(
                reminderId,
                request!,
                cancellationToken);

            if (updatedReminder == null)
            {
                return NotFound();
            }

            logger.LogInformation("Updated reminder with id {ReminderId}.", reminderId);

            return Ok(updatedReminder);
        }
        catch (ReminderReferenceNotFoundException exception)
        {
            return ReferenceValidationProblem(exception);
        }
    }

    [HttpDelete("{reminderId:guid}")]
    public async Task<IActionResult> DeleteReminderAsync(
        Guid reminderId,
        CancellationToken cancellationToken)
    {
        if (reminderId == Guid.Empty)
        {
            return BadRequest("Reminder id cannot be empty.");
        }

        bool reminderWasDeleted = await remindersService.DeleteReminderAsync(reminderId, cancellationToken);

        if (!reminderWasDeleted)
        {
            return NotFound();
        }

        logger.LogInformation("Deleted reminder with id {ReminderId}.", reminderId);

        return NoContent();
    }

    private static Dictionary<string, string[]> ValidateCreateReminderRequest(CreateReminderRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        ValidateTitle(request.Title, validationErrors);
        ValidateMessage(request.Message, validationErrors);
        ValidateTriggerType(request.TriggerType, validationErrors);
        ValidateTriggerRules(
            request.TriggerType,
            request.TriggerAtUtc,
            request.TriggerLocation,
            request.SavedLocationId,
            validationErrors);
        ValidateCoordinate(request.TriggerLocation, nameof(request.TriggerLocation), validationErrors);
        ValidateTriggerRadiusMeters(request.TriggerRadiusMeters, validationErrors);
        ValidateOptionalId(request.TaskItemId, nameof(request.TaskItemId), validationErrors);
        ValidateOptionalId(request.NoteId, nameof(request.NoteId), validationErrors);
        ValidateOptionalId(request.SavedLocationId, nameof(request.SavedLocationId), validationErrors);

        return validationErrors;
    }

    private static Dictionary<string, string[]> ValidateUpdateReminderRequest(UpdateReminderRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        ValidateTitle(request.Title, validationErrors);
        ValidateMessage(request.Message, validationErrors);
        ValidateTriggerType(request.TriggerType, validationErrors);
        ValidateStatus(request.Status, validationErrors);
        ValidateTriggerRules(
            request.TriggerType,
            request.TriggerAtUtc,
            request.TriggerLocation,
            request.SavedLocationId,
            validationErrors);
        ValidateCoordinate(request.TriggerLocation, nameof(request.TriggerLocation), validationErrors);
        ValidateTriggerRadiusMeters(request.TriggerRadiusMeters, validationErrors);
        ValidateOptionalId(request.TaskItemId, nameof(request.TaskItemId), validationErrors);
        ValidateOptionalId(request.NoteId, nameof(request.NoteId), validationErrors);
        ValidateOptionalId(request.SavedLocationId, nameof(request.SavedLocationId), validationErrors);

        return validationErrors;
    }

    private static void ValidateTitle(
        string? title,
        Dictionary<string, string[]> validationErrors)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            validationErrors["Title"] = ["Title is required."];
            return;
        }

        if (title.Length > 200)
        {
            validationErrors["Title"] = ["Title cannot be longer than 200 characters."];
        }
    }

    private static void ValidateMessage(
        string? message,
        Dictionary<string, string[]> validationErrors)
    {
        if (message is { Length: > 2000 })
        {
            validationErrors["Message"] = ["Message cannot be longer than 2000 characters."];
        }
    }

    private static void ValidateTriggerType(
        ReminderTriggerTypeDto triggerType,
        Dictionary<string, string[]> validationErrors)
    {
        if (!Enum.IsDefined(triggerType))
        {
            validationErrors["TriggerType"] = ["Trigger type is invalid."];
        }
    }

    private static void ValidateStatus(
        ReminderStatusDto status,
        Dictionary<string, string[]> validationErrors)
    {
        if (!Enum.IsDefined(status))
        {
            validationErrors["Status"] = ["Status is invalid."];
        }
    }

    private static void ValidateTriggerRules(
        ReminderTriggerTypeDto triggerType,
        DateTimeOffset? triggerAtUtc,
        GeoCoordinateDto? triggerLocation,
        Guid? savedLocationId,
        Dictionary<string, string[]> validationErrors)
    {
        if (!Enum.IsDefined(triggerType))
        {
            return;
        }

        if (triggerType == ReminderTriggerTypeDto.Time && !triggerAtUtc.HasValue)
        {
            validationErrors["TriggerAtUtc"] = ["Time reminders require a trigger date and time."];
        }

        if (triggerType == ReminderTriggerTypeDto.Location &&
            triggerLocation == null &&
            !savedLocationId.HasValue)
        {
            validationErrors["TriggerLocation"] =
                ["Location reminders require either a trigger location or saved location id."];
        }
    }

    private static void ValidateCoordinate(
        GeoCoordinateDto? coordinate,
        string fieldName,
        Dictionary<string, string[]> validationErrors)
    {
        if (coordinate == null)
        {
            return;
        }

        if (!double.IsFinite(coordinate.Latitude) || coordinate.Latitude is < -90 or > 90)
        {
            validationErrors[$"{fieldName}.{nameof(coordinate.Latitude)}"] =
                ["Latitude must be between -90 and 90."];
        }

        if (!double.IsFinite(coordinate.Longitude) || coordinate.Longitude is < -180 or > 180)
        {
            validationErrors[$"{fieldName}.{nameof(coordinate.Longitude)}"] =
                ["Longitude must be between -180 and 180."];
        }

        if (coordinate.AccuracyMeters.HasValue &&
            (!double.IsFinite(coordinate.AccuracyMeters.Value) || coordinate.AccuracyMeters < 0))
        {
            validationErrors[$"{fieldName}.{nameof(coordinate.AccuracyMeters)}"] =
                ["Accuracy meters must be greater than or equal to zero."];
        }
    }

    private static void ValidateTriggerRadiusMeters(
        double? triggerRadiusMeters,
        Dictionary<string, string[]> validationErrors)
    {
        if (triggerRadiusMeters.HasValue &&
            (!double.IsFinite(triggerRadiusMeters.Value) || triggerRadiusMeters < 0))
        {
            validationErrors["TriggerRadiusMeters"] =
                ["Trigger radius meters must be greater than or equal to zero."];
        }
    }

    private static void ValidateOptionalId(
        Guid? id,
        string fieldName,
        Dictionary<string, string[]> validationErrors)
    {
        if (id == Guid.Empty)
        {
            validationErrors[fieldName] = [$"{fieldName} cannot be empty."];
        }
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

    private ActionResult ReferenceValidationProblem(ReminderReferenceNotFoundException exception)
    {
        Dictionary<string, string[]> validationErrors = new()
        {
            [exception.FieldName] = [exception.ErrorMessage]
        };

        return ValidationProblem(ToModelStateDictionary(validationErrors));
    }
}
