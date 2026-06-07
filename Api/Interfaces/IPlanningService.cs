using NOAH.Contracts.Planning;

namespace Api.Interfaces;

public interface IPlanningService
{
    /// <summary>
    /// Gets the tasks and reminders relevant for a specific date.
    /// </summary>
    /// <param name="date">The local calendar date to plan.</param>
    /// <param name="timeZoneId">The time zone used to translate local day boundaries to UTC.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The day plan for the requested date.</returns>
    Task<DayPlanDto> GetDayPlanAsync(
        DateOnly date,
        string? timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the tasks and reminders relevant for today in the requested time zone.
    /// </summary>
    /// <param name="timeZoneId">The time zone used to determine today's local date.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The day plan for today.</returns>
    Task<DayPlanDto> GetTodayPlanAsync(string? timeZoneId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets seven day plans starting on the requested date.
    /// </summary>
    /// <param name="startsOn">The first local calendar date to include.</param>
    /// <param name="timeZoneId">The time zone used to translate local day boundaries to UTC.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The seven-day planning period.</returns>
    Task<PlanningPeriodDto> GetWeekPlanAsync(
        DateOnly startsOn,
        string? timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets upcoming day plans starting today.
    /// </summary>
    /// <param name="days">The number of days to include.</param>
    /// <param name="timeZoneId">The time zone used to determine today and day boundaries.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The upcoming planning period.</returns>
    Task<PlanningPeriodDto> GetUpcomingPlanAsync(
        int days,
        string? timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets unfinished tasks and scheduled reminders that are already past due.
    /// </summary>
    /// <param name="timeZoneId">The time zone used to determine the current local time.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The overdue planning items.</returns>
    Task<PlanningItemsDto> GetOverdueItemsAsync(string? timeZoneId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets open tasks that are not planned and have no due date.
    /// </summary>
    /// <param name="timeZoneId">The time zone included in the response metadata.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The backlog tasks.</returns>
    Task<PlanningItemsDto> GetBacklogItemsAsync(string? timeZoneId, CancellationToken cancellationToken = default);
}
