using NOAH.Contracts.Reminders;
using NOAH.Contracts.Tasks;

namespace NOAH.Contracts.Planning;

public sealed record DayPlanDto(
    DateOnly Date,
    IReadOnlyList<TaskItemDto> Tasks,
    IReadOnlyList<ReminderDto> Reminders);
