using NOAH.Contracts.Reminders;
using NOAH.Contracts.Tasks;

namespace NOAH.Contracts.Planning;

public sealed record DayPlanDto(
    DateOnly Date,
    IReadOnlyList<TaskItemDto> Tasks,
    IReadOnlyList<ReminderDto> Reminders);

public sealed record PlanningPeriodDto(
    DateOnly StartsOn,
    DateOnly EndsOn,
    string TimeZoneId,
    IReadOnlyList<DayPlanDto> Days);

public sealed record PlanningItemsDto(
    string TimeZoneId,
    IReadOnlyList<TaskItemDto> Tasks,
    IReadOnlyList<ReminderDto> Reminders);
