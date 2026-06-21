using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Client.Models;
using Client.Services;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Reminders;

namespace Client.ViewModels;

public class CalendarViewModel : INotifyPropertyChanged
{
    private readonly ITaskRepository taskRepository;
    private readonly IReminderRepository reminderRepository;
    private readonly UserDialogService userDialogService;
    private DateTime displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private CalendarDay? selectedDay;
    private IReadOnlyList<CalendarDay> calendarDays = [];
    private IReadOnlyList<TaskItem> tasks = [];
    private IReadOnlyList<ReminderDto> reminders = [];
    private int firstDayColumn;
    private bool isRebuilding;

    public CalendarViewModel(
        ITaskRepository taskRepository,
        IReminderRepository reminderRepository,
        UserDialogService userDialogService)
    {
        this.taskRepository = taskRepository;
        this.reminderRepository = reminderRepository;
        this.userDialogService = userDialogService;

        PrevMonthCommand = new Command(() => ChangeMonth(-1));
        NextMonthCommand = new Command(() => ChangeMonth(1));
        TodayCommand = new Command(GoToToday);
        SelectDayCommand = new Command<CalendarDay>(SelectDay);
        CreateTaskCommand = new Command(async () => await CreateTaskAsync());
        CreateReminderCommand = new Command(async () => await CreateReminderAsync());

        RebuildCalendar();
        _ = RefreshAsync();
    }

    public IReadOnlyList<CalendarDay> MiniCalendarDays => calendarDays;
    public IReadOnlyList<CalendarDay> FullCalendarDays => calendarDays;
    public IReadOnlyList<CalendarEvent> TodayEvents { get; private set; } = [];
    public int FirstDayColumn { get => firstDayColumn; private set { if (firstDayColumn != value) { firstDayColumn = value; OnPropertyChanged(); } } }
    public string MonthYearLabel => displayMonth.ToString("MMMM yyyy");
    public string SelectedDayHeading => (SelectedDay?.Date.Date ?? DateTime.Today) == DateTime.Today
        ? "Today"
        : (SelectedDay?.Date ?? DateTime.Today).ToString("dddd");
    public string TodayLabel => (SelectedDay?.Date ?? DateTime.Today).ToString("d MMMM");
    public CalendarDay? SelectedDay
    {
        get => selectedDay;
        set
        {
            if (selectedDay == value) return;
            if (selectedDay != null) selectedDay.IsSelected = false;
            selectedDay = value;
            if (selectedDay != null) selectedDay.IsSelected = true;
            if (!isRebuilding)
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedDayHeading));
                OnPropertyChanged(nameof(TodayLabel));
                RefreshSelectedDayEvents();
            }
        }
    }

    public ICommand PrevMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand SelectDayCommand { get; }
    public ICommand CreateTaskCommand { get; }
    public ICommand CreateReminderCommand { get; }

    public async Task RefreshAsync()
    {
        tasks = await taskRepository.GetAllAsync();
        reminders = await reminderRepository.GetAllAsync();
        RebuildCalendar();
        RefreshSelectedDayEvents();
    }

    private void ChangeMonth(int offset)
    {
        displayMonth = displayMonth.AddMonths(offset);
        RebuildCalendar();
        OnPropertyChanged(nameof(MonthYearLabel));
    }

    private void GoToToday()
    {
        DateTime today = DateTime.Today;
        displayMonth = new DateTime(today.Year, today.Month, 1);
        RebuildCalendar();
        SelectedDay = calendarDays.FirstOrDefault(day => day.Date.Date == today) ?? SelectedDay;
        OnPropertyChanged(nameof(MonthYearLabel));
        OnPropertyChanged(nameof(SelectedDayHeading));
        OnPropertyChanged(nameof(TodayLabel));
    }

    private void SelectDay(CalendarDay? day)
    {
        if (day != null) SelectedDay = day;
    }

    private void RebuildCalendar()
    {
        DateTime firstOfMonth = displayMonth;
        FirstDayColumn = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        int daysInMonth = DateTime.DaysInMonth(displayMonth.Year, displayMonth.Month);
        HashSet<DateOnly> daysWithItems = BuildDaysWithItems();

        CalendarDay? preferredDay = null;
        List<CalendarDay> days = [];
        for (int i = 0; i < daysInMonth; i++)
        {
            DateTime date = firstOfMonth.AddDays(i);
            CalendarDay day = new()
            {
                Date = date,
                DayNumber = date.Day.ToString(),
                IsCurrentMonth = true,
                IsToday = date.Date == DateTime.Today,
                HasEvents = daysWithItems.Contains(DateOnly.FromDateTime(date))
            };

            if (SelectedDay != null && SelectedDay.Date.Date == date.Date) preferredDay = day;
            if (preferredDay == null && day.IsToday) preferredDay = day;
            days.Add(day);
        }

        calendarDays = days;
        isRebuilding = true;
        SelectedDay = preferredDay ?? calendarDays.FirstOrDefault();
        isRebuilding = false;
        OnPropertyChanged(nameof(FullCalendarDays));
        OnPropertyChanged(nameof(SelectedDayHeading));
        OnPropertyChanged(nameof(TodayLabel));
    }

    private HashSet<DateOnly> BuildDaysWithItems()
    {
        HashSet<DateOnly> result = [];
        foreach (TaskItem task in tasks)
        {
            if (task.PlannedFor.HasValue) result.Add(task.PlannedFor.Value);
            if (task.DueAtUtc.HasValue) result.Add(DateOnly.FromDateTime(task.DueAtUtc.Value.LocalDateTime));
        }

        foreach (ReminderDto reminder in reminders)
        {
            if (reminder.TriggerAtUtc.HasValue) result.Add(DateOnly.FromDateTime(reminder.TriggerAtUtc.Value.LocalDateTime));
        }

        return result;
    }

    private void RefreshSelectedDayEvents()
    {
        DateOnly selectedDate = DateOnly.FromDateTime((SelectedDay?.Date ?? DateTime.Today).Date);
        List<CalendarEvent> events = [];

        events.AddRange(tasks.Where(task => task.IsRelevantForDate(selectedDate)).Select(task => new CalendarEvent
        {
            TimeDisplay = task.DueAtUtc?.ToLocalTime().ToString("HH:mm") ?? "Task",
            Title = task.Title,
            Subtitle = string.IsNullOrWhiteSpace(task.Description) ? task.Status.ToString() : task.Description!
        }));

        events.AddRange(reminders.Where(reminder => reminder.TriggerAtUtc.HasValue && DateOnly.FromDateTime(reminder.TriggerAtUtc.Value.LocalDateTime) == selectedDate).Select(reminder => new CalendarEvent
        {
            TimeDisplay = reminder.TriggerAtUtc?.ToLocalTime().ToString("HH:mm") ?? "Reminder",
            Title = reminder.Title,
            Subtitle = string.IsNullOrWhiteSpace(reminder.Message) ? reminder.Status.ToString() : reminder.Message!
        }));

        TodayEvents = events.OrderBy(calendarEvent => calendarEvent.TimeDisplay).ToList();
        OnPropertyChanged(nameof(TodayEvents));
    }

    private async Task CreateTaskAsync()
    {
        string? title = await userDialogService.PromptAsync("New task", "Enter a task title.");
        if (string.IsNullOrWhiteSpace(title)) return;
        string? description = await userDialogService.PromptAsync("New task", "Add an optional description.", accept: "Continue");
        string? timeText = await userDialogService.PromptAsync("Task time", "Optional: enter a due time in HH:mm.", "09:00", "Save");
        DateOnly date = DateOnly.FromDateTime((SelectedDay?.Date ?? DateTime.Today).Date);
        DateTimeOffset? dueAtUtc = ParseLocalDateTimeAsUtc(date, timeText, allowEmpty: true);
        if (timeText != null && !string.IsNullOrWhiteSpace(timeText) && dueAtUtc == null)
        {
            await userDialogService.ShowAlertAsync("Invalid time", "Use a 24-hour time like 09:00 or 18:30.");
            return;
        }

        await taskRepository.SaveAsync(new TaskItem
        {
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Status = TaskItemStatusDto.Open,
            Priority = TaskPriorityDto.Normal,
            PlannedFor = date,
            DueAtUtc = dueAtUtc
        });

        await RefreshAsync();
    }

    private async Task CreateReminderAsync()
    {
        string? title = await userDialogService.PromptAsync("New reminder", "Enter a reminder title.");
        if (string.IsNullOrWhiteSpace(title)) return;
        string? message = await userDialogService.PromptAsync("New reminder", "Add an optional reminder message.", accept: "Continue");
        string? timeText = await userDialogService.PromptAsync("Reminder time", "Enter a reminder time in HH:mm.", "09:00", "Save");
        if (timeText == null) return;
        DateOnly date = DateOnly.FromDateTime((SelectedDay?.Date ?? DateTime.Today).Date);
        DateTimeOffset? triggerAtUtc = ParseLocalDateTimeAsUtc(date, timeText, allowEmpty: false);
        if (triggerAtUtc == null)
        {
            await userDialogService.ShowAlertAsync("Invalid time", "Use a 24-hour time like 09:00 or 18:30.");
            return;
        }

        ReminderDto reminder = new(Guid.Empty, title.Trim(), string.IsNullOrWhiteSpace(message) ? null : message.Trim(), ReminderTriggerTypeDto.Time, ReminderStatusDto.Scheduled, true, triggerAtUtc, null, null, null, null, null, null, default, null);
        await reminderRepository.SaveAsync(reminder);
        await RefreshAsync();
    }

    private static DateTimeOffset? ParseLocalDateTimeAsUtc(DateOnly date, string? timeText, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(timeText))
        {
            if (allowEmpty) return null;
            DateTime fallback = date.ToDateTime(new TimeOnly(9, 0));
            return new DateTimeOffset(fallback, TimeZoneInfo.Local.GetUtcOffset(fallback)).ToUniversalTime();
        }

        if (!TimeOnly.TryParse(timeText, CultureInfo.CurrentCulture, DateTimeStyles.None, out TimeOnly time) && !TimeOnly.TryParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            return null;
        }

        DateTime localDateTime = date.ToDateTime(time);
        return new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime)).ToUniversalTime();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class CalendarDay : INotifyPropertyChanged
{
    private bool isSelected;
    public DateTime Date { get; set; }
    public string DayNumber { get; set; } = string.Empty;
    public bool IsCurrentMonth { get; set; }
    public bool IsToday { get; set; }
    public bool HasEvents { get; set; }
    public bool IsSelected { get => isSelected; set { if (isSelected != value) { isSelected = value; OnPropertyChanged(); } } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class CalendarEvent
{
    public string TimeDisplay { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
}
