using Api.Helpers;
using Api.Interfaces;
using Microsoft.EntityFrameworkCore;
using NOAH.Contracts.Common;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Reminders;
using NOAH.Domain.Entities;
using NOAH.Domain.Enums;
using NOAH.Domain.ValueObjects;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

public sealed class RemindersService(NoahDbContext noahDbContext) : IRemindersService
{
    public async Task<IReadOnlyList<ReminderDto>> GetRemindersAsync(CancellationToken cancellationToken = default)
    {
        List<ReminderDto> reminders = await noahDbContext.Reminders
            .AsNoTracking()
            .OrderBy(reminder => reminder.Status)
            .ThenBy(reminder => reminder.TriggerAtUtc ?? DateTimeOffset.MaxValue)
            .ThenByDescending(reminder => reminder.CreatedAtUtc)
            .Select(reminder => new ReminderDto(
                reminder.Id,
                reminder.Title,
                reminder.Message,
                (ReminderTriggerTypeDto)reminder.TriggerType,
                (ReminderStatusDto)reminder.Status,
                reminder.ShouldNotify,
                reminder.TriggerAtUtc,
                reminder.TriggerLocation == null
                    ? null
                    : new GeoCoordinateDto(
                        reminder.TriggerLocation.Latitude,
                        reminder.TriggerLocation.Longitude,
                        reminder.TriggerLocation.AccuracyMeters),
                reminder.TriggerRadiusMeters,
                reminder.LastTriggeredAtUtc,
                reminder.TaskItemId,
                reminder.NoteId,
                reminder.SavedLocationId,
                reminder.CreatedAtUtc,
                reminder.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return reminders;
    }

    public async Task<ReminderDto?> GetReminderByIdAsync(
        Guid reminderId,
        CancellationToken cancellationToken = default)
    {
        ReminderDto? reminder = await noahDbContext.Reminders
            .AsNoTracking()
            .Where(reminder => reminder.Id == reminderId)
            .Select(reminder => new ReminderDto(
                reminder.Id,
                reminder.Title,
                reminder.Message,
                (ReminderTriggerTypeDto)reminder.TriggerType,
                (ReminderStatusDto)reminder.Status,
                reminder.ShouldNotify,
                reminder.TriggerAtUtc,
                reminder.TriggerLocation == null
                    ? null
                    : new GeoCoordinateDto(
                        reminder.TriggerLocation.Latitude,
                        reminder.TriggerLocation.Longitude,
                        reminder.TriggerLocation.AccuracyMeters),
                reminder.TriggerRadiusMeters,
                reminder.LastTriggeredAtUtc,
                reminder.TaskItemId,
                reminder.NoteId,
                reminder.SavedLocationId,
                reminder.CreatedAtUtc,
                reminder.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return reminder;
    }

    public async Task<ReminderDto> CreateReminderAsync(
        CreateReminderRequest request,
        CancellationToken cancellationToken = default)
    {
        await ValidateRelatedEntitiesAsync(
            request.TaskItemId,
            request.NoteId,
            request.SavedLocationId,
            cancellationToken);

        GeoCoordinate? triggerLocation = await ResolveTriggerLocationAsync(
            (ReminderTriggerType)request.TriggerType,
            request.TriggerLocation,
            request.SavedLocationId,
            cancellationToken);

        DateTimeOffset currentDateTimeUtc = DateTimeOffset.UtcNow;

        Reminder reminder = new()
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Message = NormalizeOptionalText(request.Message),
            TriggerType = (ReminderTriggerType)request.TriggerType,
            Status = ReminderStatus.Scheduled,
            ShouldNotify = request.ShouldNotify,
            TriggerAtUtc = request.TriggerAtUtc,
            TriggerLocation = triggerLocation,
            TriggerRadiusMeters = request.TriggerRadiusMeters,
            LastTriggeredAtUtc = null,
            TaskItemId = request.TaskItemId,
            NoteId = request.NoteId,
            SavedLocationId = request.SavedLocationId,
            CreatedAtUtc = currentDateTimeUtc,
            UpdatedAtUtc = null
        };

        noahDbContext.Reminders.Add(reminder);

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(reminder);
    }

    public async Task<ReminderDto?> UpdateReminderAsync(
        Guid reminderId,
        UpdateReminderRequest request,
        CancellationToken cancellationToken = default)
    {
        Reminder? reminder = await noahDbContext.Reminders
            .FirstOrDefaultAsync(reminder => reminder.Id == reminderId, cancellationToken);

        if (reminder == null)
        {
            return null;
        }

        await ValidateRelatedEntitiesAsync(
            request.TaskItemId,
            request.NoteId,
            request.SavedLocationId,
            cancellationToken);

        GeoCoordinate? triggerLocation = await ResolveTriggerLocationAsync(
            (ReminderTriggerType)request.TriggerType,
            request.TriggerLocation,
            request.SavedLocationId,
            cancellationToken);

        ReminderStatus previousStatus = reminder.Status;

        reminder.Title = request.Title.Trim();
        reminder.Message = NormalizeOptionalText(request.Message);
        reminder.TriggerType = (ReminderTriggerType)request.TriggerType;
        reminder.Status = (ReminderStatus)request.Status;
        reminder.ShouldNotify = request.ShouldNotify;
        reminder.TriggerAtUtc = request.TriggerAtUtc;
        reminder.TriggerLocation = triggerLocation;
        reminder.TriggerRadiusMeters = request.TriggerRadiusMeters;
        reminder.TaskItemId = request.TaskItemId;
        reminder.NoteId = request.NoteId;
        reminder.SavedLocationId = request.SavedLocationId;
        reminder.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (reminder.Status == ReminderStatus.Triggered && previousStatus != ReminderStatus.Triggered)
        {
            reminder.LastTriggeredAtUtc = DateTimeOffset.UtcNow;
        }

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(reminder);
    }

    public async Task<bool> DeleteReminderAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        Reminder? reminder = await noahDbContext.Reminders
            .FirstOrDefaultAsync(reminder => reminder.Id == reminderId, cancellationToken);

        if (reminder == null)
        {
            return false;
        }

        noahDbContext.Reminders.Remove(reminder);

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task ValidateRelatedEntitiesAsync(
        Guid? taskItemId,
        Guid? noteId,
        Guid? savedLocationId,
        CancellationToken cancellationToken)
    {
        if (taskItemId.HasValue &&
            !await noahDbContext.TaskItems.AnyAsync(taskItem => taskItem.Id == taskItemId.Value, cancellationToken))
        {
            throw new ReminderReferenceNotFoundException(
                "TaskItemId",
                "Task item was not found.");
        }

        if (noteId.HasValue &&
            !await noahDbContext.Notes.AnyAsync(note => note.Id == noteId.Value, cancellationToken))
        {
            throw new ReminderReferenceNotFoundException(
                "NoteId",
                "Note was not found.");
        }

        if (savedLocationId.HasValue &&
            !await noahDbContext.SavedLocations.AnyAsync(
                savedLocation => savedLocation.Id == savedLocationId.Value,
                cancellationToken))
        {
            throw new ReminderReferenceNotFoundException(
                "SavedLocationId",
                "Saved location was not found.");
        }
    }

    private async Task<GeoCoordinate?> ResolveTriggerLocationAsync(
        ReminderTriggerType triggerType,
        GeoCoordinateDto? triggerLocation,
        Guid? savedLocationId,
        CancellationToken cancellationToken)
    {
        if (triggerLocation != null)
        {
            return MapToValueObject(triggerLocation);
        }

        if (triggerType != ReminderTriggerType.Location || !savedLocationId.HasValue)
        {
            return null;
        }

        GeoCoordinate? savedLocationCoordinate = await noahDbContext.SavedLocations
            .AsNoTracking()
            .Where(savedLocation => savedLocation.Id == savedLocationId)
            .Select(savedLocation => savedLocation.Coordinate)
            .FirstOrDefaultAsync(cancellationToken);

        if (savedLocationCoordinate == null)
        {
            throw new ReminderReferenceNotFoundException(
                "SavedLocationId",
                "Saved location was not found.");
        }

        return savedLocationCoordinate;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static GeoCoordinate MapToValueObject(GeoCoordinateDto coordinate)
    {
        return new GeoCoordinate
        {
            Latitude = coordinate.Latitude,
            Longitude = coordinate.Longitude,
            AccuracyMeters = coordinate.AccuracyMeters
        };
    }

    private static ReminderDto MapToDto(Reminder reminder)
    {
        return new ReminderDto(
            reminder.Id,
            reminder.Title,
            reminder.Message,
            (ReminderTriggerTypeDto)reminder.TriggerType,
            (ReminderStatusDto)reminder.Status,
            reminder.ShouldNotify,
            reminder.TriggerAtUtc,
            reminder.TriggerLocation == null ? null : MapToDto(reminder.TriggerLocation),
            reminder.TriggerRadiusMeters,
            reminder.LastTriggeredAtUtc,
            reminder.TaskItemId,
            reminder.NoteId,
            reminder.SavedLocationId,
            reminder.CreatedAtUtc,
            reminder.UpdatedAtUtc);
    }

    private static GeoCoordinateDto MapToDto(GeoCoordinate coordinate)
    {
        return new GeoCoordinateDto(
            coordinate.Latitude,
            coordinate.Longitude,
            coordinate.AccuracyMeters);
    }
}
