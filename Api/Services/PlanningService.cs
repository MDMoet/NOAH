using Api.Interfaces;
using Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NOAH.Contracts.Common;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Planning;
using NOAH.Contracts.Reminders;
using NOAH.Contracts.Tasks;
using NOAH.Domain.Enums;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

public sealed class PlanningService(
    NoahDbContext noahDbContext,
    IOptions<PlanningModel> planningOptions)
    : IPlanningService
{
    private readonly PlanningModel _options = planningOptions.Value;

    public async Task<DayPlanDto> GetDayPlanAsync(
        DateOnly date,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        TimeZoneInfo timeZoneInfo = GetTimeZoneInfo(timeZoneId);
        (DateTimeOffset dayStartUtc, DateTimeOffset nextDayStartUtc) = GetUtcDayWindow(date, timeZoneInfo);

        List<TaskItemDto> tasks = await GetTasksForDayAsync(
            date,
            dayStartUtc,
            nextDayStartUtc,
            cancellationToken);

        List<ReminderDto> reminders = await GetRemindersForDayAsync(
            dayStartUtc,
            nextDayStartUtc,
            cancellationToken);

        return new DayPlanDto(date, tasks, reminders);
    }

    public async Task<DayPlanDto> GetTodayPlanAsync(
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        TimeZoneInfo timeZoneInfo = GetTimeZoneInfo(timeZoneId);
        DateOnly today = GetToday(timeZoneInfo);

        return await GetDayPlanAsync(today, timeZoneId, cancellationToken);
    }

    public async Task<PlanningPeriodDto> GetWeekPlanAsync(
        DateOnly startsOn,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        TimeZoneInfo timeZoneInfo = GetTimeZoneInfo(timeZoneId);
        List<DayPlanDto> days = await GetPeriodAsync(startsOn, 7, timeZoneInfo, cancellationToken);

        return new PlanningPeriodDto(
            startsOn,
            startsOn.AddDays(6),
            GetResponseTimeZoneId(timeZoneId, timeZoneInfo),
            days);
    }

    public async Task<PlanningPeriodDto> GetUpcomingPlanAsync(
        int days,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        TimeZoneInfo timeZoneInfo = GetTimeZoneInfo(timeZoneId);
        DateOnly startsOn = GetToday(timeZoneInfo);
        int clampedDays = Math.Clamp(days, 1, Math.Max(1, _options.MaxUpcomingDays));
        List<DayPlanDto> dayPlans = await GetPeriodAsync(startsOn, clampedDays, timeZoneInfo, cancellationToken);

        return new PlanningPeriodDto(
            startsOn,
            startsOn.AddDays(clampedDays - 1),
            GetResponseTimeZoneId(timeZoneId, timeZoneInfo),
            dayPlans);
    }

    public async Task<PlanningItemsDto> GetOverdueItemsAsync(
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        TimeZoneInfo timeZoneInfo = GetTimeZoneInfo(timeZoneId);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        List<TaskItemDto> tasks = await noahDbContext.TaskItems
            .AsNoTracking()
            .Where(taskItem =>
                taskItem.Status != TaskItemStatus.Completed &&
                taskItem.Status != TaskItemStatus.Cancelled &&
                taskItem.DueAtUtc < nowUtc)
            .OrderBy(taskItem => taskItem.Status)
            .ThenByDescending(taskItem => taskItem.Priority)
            .ThenBy(taskItem => taskItem.DueAtUtc)
            .ThenBy(taskItem => taskItem.Title)
            .Select(taskItem => MapTaskItemToDto(taskItem))
            .ToListAsync(cancellationToken);

        List<ReminderDto> reminders = await noahDbContext.Reminders
            .AsNoTracking()
            .Where(reminder =>
                reminder.Status != ReminderStatus.Completed &&
                reminder.Status != ReminderStatus.Cancelled &&
                reminder.TriggerAtUtc < nowUtc)
            .OrderBy(reminder => reminder.Status)
            .ThenBy(reminder => reminder.TriggerAtUtc)
            .ThenBy(reminder => reminder.Title)
            .Select(reminder => MapReminderToDto(reminder))
            .ToListAsync(cancellationToken);

        return new PlanningItemsDto(GetResponseTimeZoneId(timeZoneId, timeZoneInfo), tasks, reminders);
    }

    public async Task<PlanningItemsDto> GetBacklogItemsAsync(
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        TimeZoneInfo timeZoneInfo = GetTimeZoneInfo(timeZoneId);

        List<TaskItemDto> tasks = await noahDbContext.TaskItems
            .AsNoTracking()
            .Where(taskItem =>
                taskItem.Status != TaskItemStatus.Completed &&
                taskItem.Status != TaskItemStatus.Cancelled &&
                taskItem.PlannedFor == null &&
                taskItem.DueAtUtc == null)
            .OrderBy(taskItem => taskItem.Status)
            .ThenByDescending(taskItem => taskItem.Priority)
            .ThenBy(taskItem => taskItem.Title)
            .Select(taskItem => MapTaskItemToDto(taskItem))
            .ToListAsync(cancellationToken);

        return new PlanningItemsDto(GetResponseTimeZoneId(timeZoneId, timeZoneInfo), tasks, []);
    }

    private async Task<List<DayPlanDto>> GetPeriodAsync(
        DateOnly startsOn,
        int days,
        TimeZoneInfo timeZoneInfo,
        CancellationToken cancellationToken)
    {
        List<DayPlanDto> dayPlans = [];

        for (int dayOffset = 0; dayOffset < days; dayOffset++)
        {
            DateOnly date = startsOn.AddDays(dayOffset);
            (DateTimeOffset dayStartUtc, DateTimeOffset nextDayStartUtc) = GetUtcDayWindow(date, timeZoneInfo);

            List<TaskItemDto> tasks = await GetTasksForDayAsync(
                date,
                dayStartUtc,
                nextDayStartUtc,
                cancellationToken);

            List<ReminderDto> reminders = await GetRemindersForDayAsync(
                dayStartUtc,
                nextDayStartUtc,
                cancellationToken);

            dayPlans.Add(new DayPlanDto(date, tasks, reminders));
        }

        return dayPlans;
    }

    private async Task<List<TaskItemDto>> GetTasksForDayAsync(
        DateOnly date,
        DateTimeOffset dayStartUtc,
        DateTimeOffset nextDayStartUtc,
        CancellationToken cancellationToken)
    {
        return await noahDbContext.TaskItems
            .AsNoTracking()
            .Where(taskItem =>
                taskItem.Status != TaskItemStatus.Cancelled &&
                (taskItem.PlannedFor == date ||
                 taskItem.DueAtUtc >= dayStartUtc && taskItem.DueAtUtc < nextDayStartUtc))
            .OrderBy(taskItem => taskItem.Status)
            .ThenByDescending(taskItem => taskItem.Priority)
            .ThenBy(taskItem => taskItem.DueAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(taskItem => taskItem.Title)
            .Select(taskItem => MapTaskItemToDto(taskItem))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<ReminderDto>> GetRemindersForDayAsync(
        DateTimeOffset dayStartUtc,
        DateTimeOffset nextDayStartUtc,
        CancellationToken cancellationToken)
    {
        return await noahDbContext.Reminders
            .AsNoTracking()
            .Where(reminder =>
                reminder.Status != ReminderStatus.Cancelled &&
                reminder.TriggerAtUtc >= dayStartUtc &&
                reminder.TriggerAtUtc < nextDayStartUtc)
            .OrderBy(reminder => reminder.Status)
            .ThenBy(reminder => reminder.TriggerAtUtc)
            .ThenBy(reminder => reminder.Title)
            .Select(reminder => MapReminderToDto(reminder))
            .ToListAsync(cancellationToken);
    }

    private TimeZoneInfo GetTimeZoneInfo(string? timeZoneId)
    {
        string effectiveTimeZoneId = string.IsNullOrWhiteSpace(timeZoneId)
            ? _options.DefaultTimeZoneId
            : timeZoneId.Trim();

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(effectiveTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(effectiveTimeZoneId, out string? windowsTimeZoneId))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsTimeZoneId);
            }

            throw;
        }
    }

    private static DateOnly GetToday(TimeZoneInfo timeZoneInfo)
    {
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZoneInfo);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private static (DateTimeOffset DayStartUtc, DateTimeOffset NextDayStartUtc) GetUtcDayWindow(
        DateOnly date,
        TimeZoneInfo timeZoneInfo)
    {
        DateTime localDayStart = date.ToDateTime(TimeOnly.MinValue);
        DateTime localNextDayStart = date.AddDays(1).ToDateTime(TimeOnly.MinValue);

        DateTimeOffset dayStartUtc = new DateTimeOffset(
            localDayStart,
            timeZoneInfo.GetUtcOffset(localDayStart)).ToUniversalTime();

        DateTimeOffset nextDayStartUtc = new DateTimeOffset(
            localNextDayStart,
            timeZoneInfo.GetUtcOffset(localNextDayStart)).ToUniversalTime();

        return (dayStartUtc, nextDayStartUtc);
    }

    private string GetResponseTimeZoneId(string? requestedTimeZoneId, TimeZoneInfo resolvedTimeZoneInfo)
    {
        if (!string.IsNullOrWhiteSpace(requestedTimeZoneId))
        {
            return requestedTimeZoneId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_options.DefaultTimeZoneId))
        {
            return _options.DefaultTimeZoneId;
        }

        return resolvedTimeZoneInfo.Id;
    }

    private static TaskItemDto MapTaskItemToDto(NOAH.Domain.Entities.TaskItem taskItem)
    {
        return new TaskItemDto(
            taskItem.Id,
            taskItem.Title,
            taskItem.Description,
            (TaskItemStatusDto)taskItem.Status,
            (TaskPriorityDto)taskItem.Priority,
            taskItem.DueAtUtc,
            taskItem.PlannedFor,
            taskItem.CreatedAtUtc,
            taskItem.UpdatedAtUtc);
    }

    private static ReminderDto MapReminderToDto(NOAH.Domain.Entities.Reminder reminder)
    {
        return new ReminderDto(
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
            reminder.UpdatedAtUtc);
    }
}
