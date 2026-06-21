using Client.Models;
using NOAH.Contracts.Enums;

namespace Client.UnitTests;

public sealed class TaskItemTests
{
    [Fact]
    public void IsRelevantForDate_MatchesPlannedDateOrDueDate()
    {
        DateOnly targetDate = new(2026, 6, 20);
        TaskItem plannedTask = new()
        {
            Title = "Planned task",
            PlannedFor = targetDate,
            Status = TaskItemStatusDto.Open
        };
        TaskItem dueTask = new()
        {
            Title = "Due task",
            DueAtUtc = new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero),
            Status = TaskItemStatusDto.Open
        };

        Assert.True(plannedTask.IsRelevantForDate(targetDate));
        Assert.True(dueTask.IsRelevantForDate(targetDate));
        Assert.False(dueTask.IsRelevantForDate(targetDate.AddDays(1)));
    }
}
