using Client.Models;
using Client.Services;
using Client.ViewModels;
using Microsoft.Maui.Controls;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Reminders;

namespace Client.UiTests;

public sealed class CalendarViewModelTests
{
    [Fact]
    public async Task SelectingOtherDay_UpdatesAgendaHeadingAndEvents()
    {
        DateTime today = DateTime.Today;
        DateTime selectedDate = new(today.Year, today.Month, today.Day == 1 ? 2 : 1);

        FakeTaskRepository taskRepository = new(
        [
            new TaskItem
            {
                Title = "Review design",
                PlannedFor = DateOnly.FromDateTime(selectedDate),
                Status = TaskItemStatusDto.Open
            }
        ]);

        FakeReminderRepository reminderRepository = new(
        [
            new ReminderDto(
                Guid.NewGuid(),
                "Call back",
                "Talk through the next step",
                ReminderTriggerTypeDto.Time,
                ReminderStatusDto.Scheduled,
                true,
                new DateTimeOffset(selectedDate.Year, selectedDate.Month, selectedDate.Day, 9, 0, 0, TimeSpan.Zero),
                null,
                null,
                null,
                null,
                null,
                null,
                default,
                null)
        ]);

        CalendarViewModel viewModel = new(taskRepository, reminderRepository, new UserDialogService());
        await viewModel.RefreshAsync();

        CalendarDay selectedDay = viewModel.FullCalendarDays.First(day => day.Date.Date == selectedDate.Date);
        ((Command<CalendarDay>)viewModel.SelectDayCommand).Execute(selectedDay);

        Assert.Equal(selectedDate.ToString("dddd"), viewModel.SelectedDayHeading);
        Assert.Equal(2, viewModel.TodayEvents.Count);
    }

    private sealed class FakeTaskRepository(IReadOnlyList<TaskItem> tasks) : ITaskRepository
    {
        public Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(tasks);

        public Task<IReadOnlyList<TaskItem>> GetTodayAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>([]);

        public Task<TaskItem> SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
            => Task.FromResult(task);

        public Task ToggleCompleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeReminderRepository(IReadOnlyList<ReminderDto> reminders) : IReminderRepository
    {
        public Task<IReadOnlyList<ReminderDto>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(reminders);

        public Task<ReminderDto> SaveAsync(ReminderDto reminder, CancellationToken cancellationToken = default)
            => Task.FromResult(reminder);
    }
}
