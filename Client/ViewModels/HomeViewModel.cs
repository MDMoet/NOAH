using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Client.Models;
using Client.Services;
using Microsoft.Maui.Media;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Reminders;

namespace Client.ViewModels;

public enum HomeActionDialogKind
{
    None = 0,
    Task = 1,
    Reminder = 2,
    Mileage = 3
}

public class HomeViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan NoteAutosaveDelay = TimeSpan.FromMilliseconds(500);
    private const int RecentNoteCount = 20;

    private readonly INoteRepository noteRepository;
    private readonly ITaskRepository taskRepository;
    private readonly IReminderRepository reminderRepository;
    private readonly IMileageRepository mileageRepository;
    private readonly IOdometerRecognitionService odometerRecognitionService;
    private readonly IUserLocationService userLocationService;
    private readonly IAiChatService aiChatService;
    private readonly UserDialogService userDialogService;
    private readonly AssistantApiSettingsService assistantApiSettingsService;
    private readonly CalendarViewModel calendarViewModel;
    private CancellationTokenSource? searchCancellationTokenSource;
    private string preferredResponseMode = "Text";
    private string speechCulture = "English (en-EN)";
    private CancellationTokenSource? noteSaveCancellationTokenSource;
    private Note? noteBeingEdited;
    private bool suppressNoteEditorAutosave;
    private HomeActionDialogKind homeDialogKind = HomeActionDialogKind.None;
    private string homeDialogTitle = string.Empty;
    private string homeDialogSubtitle = string.Empty;
    private string homeDialogPrimaryText = "Save";
    private string homeDialogError = string.Empty;
    private string homeStatusText = string.Empty;
    private string dialogTitleText = string.Empty;
    private string dialogDescriptionText = string.Empty;
    private DateTime dialogDate = DateTime.Today;
    private TimeSpan dialogTime = new(9, 0, 0);
    private bool taskHasDueTime = true;
    private string selectedTaskPriority = "Normal";
    private bool reminderShouldNotify = true;
    private string mileageOdometerText = string.Empty;
    private string mileageTripText = string.Empty;
    private string mileageNoteText = string.Empty;
    private bool mileageAttachCurrentLocation;
    private MileageEntrySourceDto pendingMileageSource = MileageEntrySourceDto.Manual;
    private string? pendingMileageSourceImagePath;
    private string? pendingMileageRecognizedText;
    private bool isHomeLoading;

    public HomeViewModel(
        INoteRepository noteRepository,
        ITaskRepository taskRepository,
        IReminderRepository reminderRepository,
        IMileageRepository mileageRepository,
        IOdometerRecognitionService odometerRecognitionService,
        IUserLocationService userLocationService,
        IAiChatService aiChatService,
        UserDialogService userDialogService,
        AssistantApiSettingsService assistantApiSettingsService,
        CalendarViewModel calendarViewModel)
    {
        this.noteRepository = noteRepository;
        this.taskRepository = taskRepository;
        this.reminderRepository = reminderRepository;
        this.mileageRepository = mileageRepository;
        this.odometerRecognitionService = odometerRecognitionService;
        this.userLocationService = userLocationService;
        this.aiChatService = aiChatService;
        this.userDialogService = userDialogService;
        this.assistantApiSettingsService = assistantApiSettingsService;
        this.calendarViewModel = calendarViewModel;

        ToggleChatCommand = new Command(async () => await NavigateChatAsync());
        ToggleChatMenuCommand = new Command(() => IsChatMenuOpen = !IsChatMenuOpen);
        ToggleSettingsCommand = new Command(() => IsSettingsOpen = !IsSettingsOpen);
        SendChatCommand = new Command(async () => await SendChatAsync());
        NavigateHomeCommand = new Command(NavigateHome);
        NavigateCounterCommand = new Command(() => { });
        NavigateNotesCommand = new Command(NavigateNotes);
        NavigateCalendarCommand = new Command(NavigateCalendar);
        NavigateChatCommand = new Command(async () => await NavigateChatAsync());
        NavigateMileageCommand = new Command(NavigateMileage);
        OpenNoteCommand = new Command<Note>(OpenNote);
        BackToNotesCommand = new Command(CloseNoteDetail);
        CreateNoteCommand = new Command(BeginNewNote);
        CreateReminderCommand = new Command(async () => await CreateReminderAsync());
        CreateTaskCommand = new Command(async () => await CreateTaskAsync());
        ScanOdometerCommand = new Command(async () => await ScanOdometerAsync());
        ManualMileageEntryCommand = new Command(async () => await CreateMileageEntryAsync(MileageEntrySourceDto.Manual));
        SubmitHomeDialogCommand = new Command(async () => await SubmitHomeDialogAsync());
        CancelHomeDialogCommand = new Command(CloseHomeDialog);
        SaveSettingsCommand = new Command(async () => await SaveSettingsAsync());
        SelectTextResponseModeCommand = new Command(() => PreferredResponseMode = "Text");
        SelectSpeechResponseModeCommand = new Command(() => PreferredResponseMode = "Speech");
        SelectTextAndSpeechResponseModeCommand = new Command(() => PreferredResponseMode = "TextAndSpeech");

        AssistantClientSettings settings = assistantApiSettingsService.EnsureSeededDefaults();
        ApiBaseUrl = settings.ApiBaseUrl;
        ApiKey = settings.ApiKey;

        ChatMessages.Add(new ChatMessage
        {
            From = ChatMessage.Sender.AI,
            Content = "Hey! I'm NOAH. Ask me anything about your notes, tasks, or mileage.",
            SentAt = DateTime.Now
        });
    }

    public ObservableCollection<Note> RecentNotes { get; } = [];

    public ObservableCollection<Note> AllNotesExceptRecent { get; } = [];

    public ObservableCollection<TaskItem> TodayTasks { get; } = [];

    public ObservableCollection<MileageEntry> RecentMileage { get; } = [];

    public ObservableCollection<string> TaskPriorityOptions { get; } =
    [
        "Low",
        "Normal",
        "High"
    ];

    public bool HasRecentNotes => RecentNotes.Count > 0;

    public bool HasNoRecentNotes => RecentNotes.Count == 0;

    public bool HasAllNotesExceptRecent => AllNotesExceptRecent.Count > 0;

    public bool HasTodayTasks => TodayTasks.Count > 0;

    public bool HasNoTodayTasks => TodayTasks.Count == 0;

    public bool HasRecentMileage => RecentMileage.Count > 0;

    public bool HasNoRecentMileage => RecentMileage.Count == 0;

    private Note? selectedNote;
    public Note? SelectedNote
    {
        get => selectedNote;
        set
        {
            if (selectedNote != null)
            {
                selectedNote.IsSelected = false;
            }

            selectedNote = value;

            if (selectedNote != null)
            {
                selectedNote.IsSelected = true;
            }

            OnPropertyChanged();

            if (IsNotesPageOpen && selectedNote != null && !suppressNoteEditorAutosave)
            {
                IsNoteDetailOpen = true;
            }

            if (!suppressNoteEditorAutosave)
            {
                PopulateNoteEditorFromSelectedNote();
            }
        }
    }

    public string SelectedNoteTitle => SelectedNote?.Title ?? "Select a note";

    public string SelectedNoteContent => SelectedNote?.Content ?? string.Empty;

    private string noteEditorTitle = string.Empty;
    public string NoteEditorTitle
    {
        get => noteEditorTitle;
        set
        {
            if (noteEditorTitle == value)
            {
                return;
            }

            noteEditorTitle = value;
            OnPropertyChanged();
            ScheduleNoteEditorSave();
        }
    }

    private string noteEditorContent = string.Empty;
    public string NoteEditorContent
    {
        get => noteEditorContent;
        set
        {
            if (noteEditorContent == value)
            {
                return;
            }

            noteEditorContent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNoteEditorEmpty));
            ScheduleNoteEditorSave();
        }
    }

    private string noteEditorStatusText = string.Empty;
    public string NoteEditorStatusText
    {
        get => noteEditorStatusText;
        set
        {
            if (noteEditorStatusText == value)
            {
                return;
            }

            noteEditorStatusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNoteEditorStatusText));
        }
    }

    public bool HasNoteEditorStatusText => !string.IsNullOrWhiteSpace(NoteEditorStatusText);

    public bool IsNoteEditorEmpty => string.IsNullOrWhiteSpace(NoteEditorContent);

    private string searchQuery = string.Empty;
    public string SearchQuery
    {
        get => searchQuery;
        set
        {
            if (searchQuery == value)
            {
                return;
            }

            searchQuery = value;
            OnPropertyChanged();
            _ = DebouncedSearchNotesAsync(value);
        }
    }

    private double odometerKm;
    public double OdometerKm
    {
        get => odometerKm;
        set { odometerKm = value; OnPropertyChanged(); }
    }

    private double lastTripKm;
    public double LastTripKm
    {
        get => lastTripKm;
        set { lastTripKm = value; OnPropertyChanged(); }
    }

    private double thisMonthKm;
    public double ThisMonthKm
    {
        get => thisMonthKm;
        set { thisMonthKm = value; OnPropertyChanged(); }
    }

    private DateTime lastRecorded;
    public DateTime LastRecorded
    {
        get => lastRecorded;
        set { lastRecorded = value; OnPropertyChanged(); }
    }

    public bool IsHomeLoading
    {
        get => isHomeLoading;
        set { isHomeLoading = value; OnPropertyChanged(); }
    }

    public string HomeStatusText
    {
        get => homeStatusText;
        set
        {
            homeStatusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHomeStatusText));
        }
    }

    public bool HasHomeStatusText => !string.IsNullOrWhiteSpace(HomeStatusText);

    public HomeActionDialogKind HomeDialogKind
    {
        get => homeDialogKind;
        private set
        {
            if (homeDialogKind == value) return;
            homeDialogKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHomeDialogVisible));
            OnPropertyChanged(nameof(IsTaskDialogVisible));
            OnPropertyChanged(nameof(IsReminderDialogVisible));
            OnPropertyChanged(nameof(IsMileageDialogVisible));
        }
    }

    public bool IsHomeDialogVisible => HomeDialogKind != HomeActionDialogKind.None;

    public bool IsTaskDialogVisible => HomeDialogKind == HomeActionDialogKind.Task;

    public bool IsReminderDialogVisible => HomeDialogKind == HomeActionDialogKind.Reminder;

    public bool IsMileageDialogVisible => HomeDialogKind == HomeActionDialogKind.Mileage;

    public string HomeDialogTitle
    {
        get => homeDialogTitle;
        set { homeDialogTitle = value; OnPropertyChanged(); }
    }

    public string HomeDialogSubtitle
    {
        get => homeDialogSubtitle;
        set { homeDialogSubtitle = value; OnPropertyChanged(); }
    }

    public string HomeDialogPrimaryText
    {
        get => homeDialogPrimaryText;
        set { homeDialogPrimaryText = value; OnPropertyChanged(); }
    }

    public string HomeDialogError
    {
        get => homeDialogError;
        set
        {
            homeDialogError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHomeDialogError));
        }
    }

    public bool HasHomeDialogError => !string.IsNullOrWhiteSpace(HomeDialogError);

    public string DialogTitleText
    {
        get => dialogTitleText;
        set { dialogTitleText = value; OnPropertyChanged(); }
    }

    public string DialogDescriptionText
    {
        get => dialogDescriptionText;
        set { dialogDescriptionText = value; OnPropertyChanged(); }
    }

    public DateTime DialogDate
    {
        get => dialogDate;
        set { dialogDate = value.Date; OnPropertyChanged(); }
    }

    public TimeSpan DialogTime
    {
        get => dialogTime;
        set { dialogTime = new TimeSpan(value.Hours, value.Minutes, 0); OnPropertyChanged(); }
    }

    public bool TaskHasDueTime
    {
        get => taskHasDueTime;
        set { taskHasDueTime = value; OnPropertyChanged(); }
    }

    public string SelectedTaskPriority
    {
        get => selectedTaskPriority;
        set { selectedTaskPriority = string.IsNullOrWhiteSpace(value) ? "Normal" : value.Trim(); OnPropertyChanged(); }
    }

    public bool ReminderShouldNotify
    {
        get => reminderShouldNotify;
        set { reminderShouldNotify = value; OnPropertyChanged(); }
    }

    public string MileageOdometerText
    {
        get => mileageOdometerText;
        set { mileageOdometerText = value; OnPropertyChanged(); }
    }

    public string MileageTripText
    {
        get => mileageTripText;
        set { mileageTripText = value; OnPropertyChanged(); }
    }

    public string MileageNoteText
    {
        get => mileageNoteText;
        set { mileageNoteText = value; OnPropertyChanged(); }
    }

    public bool MileageAttachCurrentLocation
    {
        get => mileageAttachCurrentLocation;
        set { mileageAttachCurrentLocation = value; OnPropertyChanged(); }
    }

    private bool isChatOpen;
    public bool IsChatOpen
    {
        get => isChatOpen;
        set { isChatOpen = value; OnPropertyChanged(); }
    }

    private bool isChatMenuOpen;
    public bool IsChatMenuOpen
    {
        get => isChatMenuOpen;
        set { isChatMenuOpen = value; OnPropertyChanged(); }
    }

    private bool isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => isSettingsOpen;
        set { isSettingsOpen = value; OnPropertyChanged(); }
    }

    private bool isNotesPageOpen;
    public bool IsNotesPageOpen
    {
        get => isNotesPageOpen;
        set { isNotesPageOpen = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsHomePageVisible)); OnPropertyChanged(nameof(IsNotesListVisible)); }
    }

    private bool isCalendarPageOpen;
    public bool IsCalendarPageOpen
    {
        get => isCalendarPageOpen;
        set { isCalendarPageOpen = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsHomePageVisible)); }
    }

    private bool isMileagePageOpen;
    public bool IsMileagePageOpen
    {
        get => isMileagePageOpen;
        set { isMileagePageOpen = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsHomePageVisible)); }
    }

    private bool isNoteDetailOpen;
    public bool IsNoteDetailOpen
    {
        get => isNoteDetailOpen;
        set { isNoteDetailOpen = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotesListVisible)); }
    }

    public bool IsHomePageVisible => !IsNotesPageOpen && !IsCalendarPageOpen && !IsMileagePageOpen;

    public bool IsNotesListVisible => IsNotesPageOpen && !IsNoteDetailOpen;

    private double desktopHomeSidebarWidth = 320;
    public double DesktopHomeSidebarWidth
    {
        get => desktopHomeSidebarWidth;
        set
        {
            double width = Math.Clamp(value, 240, 430);
            if (Math.Abs(desktopHomeSidebarWidth - width) < 0.1)
            {
                return;
            }

            desktopHomeSidebarWidth = width;
            OnPropertyChanged();
        }
    }
    private string chatInput = string.Empty;
    public string ChatInput
    {
        get => chatInput;
        set { chatInput = value; OnPropertyChanged(); }
    }

    private string chatSearchQuery = string.Empty;
    public string ChatSearchQuery
    {
        get => chatSearchQuery;
        set { chatSearchQuery = value; OnPropertyChanged(); }
    }

    private bool isChatLoading;
    public bool IsChatLoading
    {
        get => isChatLoading;
        set { isChatLoading = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ChatMessage> ChatMessages { get; } = [];

    public ObservableCollection<string> SpeechCultureOptions { get; } =
    [
        "English (en-EN)",
        "Dutch (nl-NL)",
        "English (en-US)",
        "English (en-GB)"
    ];

    private string apiBaseUrl = string.Empty;
    public string ApiBaseUrl
    {
        get => apiBaseUrl;
        set { apiBaseUrl = value; OnPropertyChanged(); }
    }

    private string apiKey = string.Empty;
    public string ApiKey
    {
        get => apiKey;
        set { apiKey = value; OnPropertyChanged(); }
    }

    public string PreferredResponseMode
    {
        get => preferredResponseMode;
        set
        {
            string normalized = NormalizeResponseMode(value);
            if (preferredResponseMode == normalized) return;
            preferredResponseMode = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTextResponseModeSelected));
            OnPropertyChanged(nameof(IsSpeechResponseModeSelected));
            OnPropertyChanged(nameof(IsTextAndSpeechResponseModeSelected));
        }
    }

    public string SpeechCulture
    {
        get => speechCulture;
        set
        {
            speechCulture = FormatSpeechCulture(value);
            OnPropertyChanged();
        }
    }

    public bool IsTextResponseModeSelected => PreferredResponseMode == "Text";

    public bool IsSpeechResponseModeSelected => PreferredResponseMode == "Speech";

    public bool IsTextAndSpeechResponseModeSelected => PreferredResponseMode == "TextAndSpeech";

    public ICommand ToggleChatCommand { get; }
    public ICommand ToggleChatMenuCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand SendChatCommand { get; }
    public ICommand NavigateHomeCommand { get; }
    public ICommand NavigateCounterCommand { get; }
    public ICommand NavigateNotesCommand { get; }
    public ICommand NavigateCalendarCommand { get; }
    public ICommand NavigateMileageCommand { get; }
    public ICommand NavigateChatCommand { get; }
    public ICommand OpenNoteCommand { get; }
    public ICommand BackToNotesCommand { get; }
    public ICommand CreateNoteCommand { get; }
    public ICommand CreateReminderCommand { get; }
    public ICommand CreateTaskCommand { get; }
    public ICommand ScanOdometerCommand { get; }
    public ICommand ManualMileageEntryCommand { get; }
    public ICommand SubmitHomeDialogCommand { get; }
    public ICommand CancelHomeDialogCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SelectTextResponseModeCommand { get; }
    public ICommand SelectSpeechResponseModeCommand { get; }
    public ICommand SelectTextAndSpeechResponseModeCommand { get; }

    public async Task LoadDataAsync()
    {
        IsHomeLoading = true;
        HomeStatusText = string.Empty;
        List<string> failures = [];

        await RunHomeLoadStepAsync(LoadNotesAsync, "notes", failures);
        await RunHomeLoadStepAsync(LoadTasksAsync, "tasks", failures);
        await RunHomeLoadStepAsync(LoadMileageAsync, "mileage", failures);
        await RunHomeLoadStepAsync(calendarViewModel.RefreshAsync, "planning", failures);

        HomeStatusText = failures.Count == 0
            ? string.Empty
            : $"Could not load {string.Join("; ", failures)}.";
        IsHomeLoading = false;
        RefreshHomeCollectionState();
    }

    private static async Task RunHomeLoadStepAsync(
        Func<Task> loadStep,
        string label,
        ICollection<string> failures)
    {
        try
        {
            await loadStep();
        }
        catch (Exception exception)
        {
            failures.Add($"{label} ({exception.Message})");
        }
    }

    private async Task LoadNotesAsync()
    {
        IReadOnlyList<Note> notes = await noteRepository.GetAllAsync();
        PopulateNoteCollections(notes);

        if (SelectedNote == null && RecentNotes.Count > 0)
        {
            SelectedNote = RecentNotes[0];
        }

        RefreshHomeCollectionState();
    }

    private void PopulateNoteEditorFromSelectedNote()
    {
        suppressNoteEditorAutosave = true;
        noteBeingEdited = SelectedNote;
        NoteEditorTitle = SelectedNote?.Title ?? string.Empty;
        NoteEditorContent = SelectedNote?.Content ?? string.Empty;
        NoteEditorStatusText = SelectedNote == null
            ? "Draft not saved"
            : FormatNoteSavedStatus(SelectedNote);
        suppressNoteEditorAutosave = false;
    }

    private void ScheduleNoteEditorSave()
    {
        if (suppressNoteEditorAutosave || !IsNoteDetailOpen)
        {
            return;
        }

        noteSaveCancellationTokenSource?.Cancel();
        noteSaveCancellationTokenSource?.Dispose();

        if (noteBeingEdited == null && string.IsNullOrWhiteSpace(NoteEditorContent))
        {
            NoteEditorStatusText = "Draft not saved";
            return;
        }

        NoteEditorStatusText = "Saving...";

        CancellationTokenSource cancellationTokenSource = new();
        noteSaveCancellationTokenSource = cancellationTokenSource;
        _ = SaveNoteEditorAfterDelayAsync(cancellationTokenSource.Token);
    }

    private async Task SaveNoteEditorAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(NoteAutosaveDelay, cancellationToken);
            await PersistNoteEditorAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            HomeStatusText = $"Search failed: {exception.Message}";
        }
    }

    private async Task PersistNoteEditorAsync(CancellationToken cancellationToken)
    {
        string trimmedContent = NoteEditorContent.Trim();

        if (noteBeingEdited == null && string.IsNullOrWhiteSpace(trimmedContent))
        {
            NoteEditorStatusText = "Draft not saved";
            return;
        }

        if (string.IsNullOrWhiteSpace(trimmedContent))
        {
            NoteEditorStatusText = "Add some text to save";
            return;
        }

        string normalizedTitle = NormalizeNoteTitle(NoteEditorTitle, trimmedContent);

        Note noteToSave = noteBeingEdited == null
            ? new Note
            {
                Title = normalizedTitle,
                Content = trimmedContent
            }
            : new Note
            {
                Id = noteBeingEdited.Id,
                Title = normalizedTitle,
                Content = trimmedContent,
                CapturedFromVoice = noteBeingEdited.CapturedFromVoice,
                SourceInteractionId = noteBeingEdited.SourceInteractionId,
                CreatedAtUtc = noteBeingEdited.CreatedAtUtc,
                UpdatedAtUtc = noteBeingEdited.UpdatedAtUtc
            };

        try
        {
            Note savedNote = await noteRepository.SaveAsync(noteToSave, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            Note storedNote = UpsertNote(savedNote);
            noteBeingEdited = storedNote;

            suppressNoteEditorAutosave = true;
            SelectedNote = storedNote;
            NoteEditorTitle = storedNote.Title;
            NoteEditorContent = storedNote.Content;
            NoteEditorStatusText = FormatNoteSavedStatus(storedNote);
            suppressNoteEditorAutosave = false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            NoteEditorStatusText = $"Save failed: {exception.Message}";
        }
    }

    private Note UpsertNote(Note savedNote)
    {
        Note? existingNote = RecentNotes.FirstOrDefault(note => note.Id == savedNote.Id);
        Note? existingOtherNote = AllNotesExceptRecent.FirstOrDefault(note => note.Id == savedNote.Id);

        if (existingNote != null)
        {
            ApplyNoteValues(existingNote, savedNote);

            int existingIndex = RecentNotes.IndexOf(existingNote);
            if (existingIndex > 0)
            {
                RecentNotes.Move(existingIndex, 0);
            }

            RefreshNoteListPartition();
            RefreshHomeCollectionState();
            return existingNote;
        }

        if (existingOtherNote != null)
        {
            AllNotesExceptRecent.Remove(existingOtherNote);
            ApplyNoteValues(existingOtherNote, savedNote);
            RecentNotes.Insert(0, existingOtherNote);
            RefreshNoteListPartition();
            RefreshHomeCollectionState();
            return existingOtherNote;
        }

        RecentNotes.Insert(0, savedNote);
        RefreshNoteListPartition();
        RefreshHomeCollectionState();
        return savedNote;
    }

    private void PopulateNoteCollections(IReadOnlyList<Note> notes)
    {
        RecentNotes.Clear();
        AllNotesExceptRecent.Clear();

        HashSet<Guid> recentNoteIds = [];

        foreach (Note note in notes.Take(RecentNoteCount))
        {
            RecentNotes.Add(note);
            recentNoteIds.Add(note.Id);
        }

        foreach (Note note in notes.Where(note => !recentNoteIds.Contains(note.Id)))
        {
            AllNotesExceptRecent.Add(note);
        }
    }

    private void RefreshNoteListPartition()
    {
        HashSet<Guid> recentNoteIds = RecentNotes
            .Take(RecentNoteCount)
            .Select(static note => note.Id)
            .ToHashSet();

        while (RecentNotes.Count > RecentNoteCount)
        {
            Note overflowNote = RecentNotes[^1];
            RecentNotes.RemoveAt(RecentNotes.Count - 1);

            if (!AllNotesExceptRecent.Any(note => note.Id == overflowNote.Id))
            {
                AllNotesExceptRecent.Insert(0, overflowNote);
            }
        }

        for (int index = AllNotesExceptRecent.Count - 1; index >= 0; index--)
        {
            if (recentNoteIds.Contains(AllNotesExceptRecent[index].Id))
            {
                AllNotesExceptRecent.RemoveAt(index);
            }
        }
    }

    private static void ApplyNoteValues(Note target, Note source)
    {
        target.Title = source.Title;
        target.Content = source.Content;
        target.CapturedFromVoice = source.CapturedFromVoice;
        target.SourceInteractionId = source.SourceInteractionId;
        target.CreatedAtUtc = source.CreatedAtUtc;
        target.UpdatedAtUtc = source.UpdatedAtUtc;
    }

    private static string NormalizeNoteTitle(string rawTitle, string content)
    {
        if (!string.IsNullOrWhiteSpace(rawTitle))
        {
            return rawTitle.Trim();
        }

        string firstLine = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(firstLine))
        {
            return firstLine.Length <= 60
                ? firstLine
                : $"{firstLine[..57].TrimEnd()}...";
        }

        return "Untitled note";
    }

    private static string FormatNoteSavedStatus(Note note)
    {
        DateTimeOffset timestamp = note.UpdatedAtUtc ?? note.CreatedAtUtc;
        return $"Saved {timestamp.ToLocalTime():HH:mm}";
    }

    private async Task LoadTasksAsync()
    {
        IReadOnlyList<TaskItem> tasks = await taskRepository.GetTodayAsync();
        TodayTasks.Clear();
        foreach (TaskItem task in tasks)
        {
            TodayTasks.Add(task);
        }

        RefreshHomeCollectionState();
    }

    private async Task LoadMileageAsync()
    {
        try
        {
            OdometerKm = await mileageRepository.GetOdometerAsync();
            LastTripKm = await mileageRepository.GetLastTripKmAsync();
            ThisMonthKm = await mileageRepository.GetThisMonthKmAsync();
            LastRecorded = await mileageRepository.GetLastRecordedAtAsync();
        }
        catch
        {
            OdometerKm = 0;
            LastTripKm = 0;
            ThisMonthKm = 0;
            LastRecorded = DateTime.Now;
        }

        IReadOnlyList<MileageEntry> entries = await mileageRepository.GetRecentAsync(20);
        RecentMileage.Clear();
        foreach (MileageEntry entry in entries)
        {
            RecentMileage.Add(entry);
        }

        RefreshHomeCollectionState();
    }

    private void RefreshHomeCollectionState()
    {
        OnPropertyChanged(nameof(HasRecentNotes));
        OnPropertyChanged(nameof(HasNoRecentNotes));
        OnPropertyChanged(nameof(HasAllNotesExceptRecent));
        OnPropertyChanged(nameof(HasTodayTasks));
        OnPropertyChanged(nameof(HasNoTodayTasks));
        OnPropertyChanged(nameof(HasRecentMileage));
        OnPropertyChanged(nameof(HasNoRecentMileage));
    }
    private void BeginNewNote()
    {
        noteSaveCancellationTokenSource?.Cancel();
        noteSaveCancellationTokenSource?.Dispose();
        noteSaveCancellationTokenSource = null;
        noteBeingEdited = null;

        suppressNoteEditorAutosave = true;
        SelectedNote = null;
        NoteEditorTitle = string.Empty;
        NoteEditorContent = string.Empty;
        NoteEditorStatusText = "Draft not saved";
        suppressNoteEditorAutosave = false;

        IsNotesPageOpen = true;
        IsCalendarPageOpen = false;
        IsMileagePageOpen = false;
        IsChatOpen = false;
        IsChatMenuOpen = false;
        IsSettingsOpen = false;
        IsNoteDetailOpen = true;
    }

    private Task CreateTaskAsync(DateOnly? plannedFor = null)
    {
        OpenTaskDialog(plannedFor ?? DateOnly.FromDateTime(DateTime.Today));
        return Task.CompletedTask;
    }

    private Task CreateReminderAsync(DateOnly? triggerDate = null)
    {
        OpenReminderDialog(triggerDate ?? DateOnly.FromDateTime(DateTime.Today));
        return Task.CompletedTask;
    }

    public void OpenTaskDialogForDate(DateTime date)
    {
        OpenTaskDialog(DateOnly.FromDateTime(date));
    }

    public void OpenReminderDialogForDate(DateTime date)
    {
        OpenReminderDialog(DateOnly.FromDateTime(date));
    }

    private void OpenTaskDialog(DateOnly plannedFor)
    {
        ResetHomeDialogInputs(plannedFor, new TimeOnly(9, 0));
        HomeDialogTitle = "New task";
        HomeDialogSubtitle = "Plan the task with the details NOAH needs.";
        HomeDialogPrimaryText = "Create task";
        SelectedTaskPriority = "Normal";
        TaskHasDueTime = false;
        HomeDialogKind = HomeActionDialogKind.Task;
    }

    private void OpenReminderDialog(DateOnly triggerDate)
    {
        ResetHomeDialogInputs(triggerDate, new TimeOnly(9, 0));
        HomeDialogTitle = "New reminder";
        HomeDialogSubtitle = "Set when NOAH should remind you.";
        HomeDialogPrimaryText = "Create reminder";
        ReminderShouldNotify = true;
        HomeDialogKind = HomeActionDialogKind.Reminder;
    }
    private async Task ScanOdometerAsync()
    {
        string? sourceImagePath = null;

        try
        {
            PermissionStatus permissionStatus = await Permissions.RequestAsync<Permissions.Camera>();

            if (permissionStatus != PermissionStatus.Granted)
            {
                HomeStatusText = "Camera access was denied.";
                return;
            }

            if (MediaPicker.Default.IsCaptureSupported)
            {
                FileResult? photo = await MediaPicker.Default.CapturePhotoAsync();
                if (photo == null)
                {
                    return;
                }

                sourceImagePath = photo.FullPath;
            }
        }
        catch (Exception exception)
        {
            HomeStatusText = $"Camera unavailable: {exception.Message}";
            return;
        }

        if (string.IsNullOrWhiteSpace(sourceImagePath))
        {
            HomeStatusText = "NOAH did not receive a photo.";
            return;
        }

        OdometerRecognitionResult recognitionResult =
            await odometerRecognitionService.RecognizeAsync(sourceImagePath);

        OpenMileageDialog(
            MileageEntrySourceDto.PhotoOcr,
            sourceImagePath,
            "Captured from photo",
            recognitionResult.RecognizedText,
            FormatWholeOdometerText(recognitionResult.OdometerKm));

        if (!recognitionResult.IsSuccessful || !recognitionResult.OdometerKm.HasValue)
        {
            HomeDialogError = recognitionResult.ErrorMessage ?? "NOAH could not confidently read the odometer. Please correct it manually.";
            return;
        }

        HomeStatusText = "Odometer read from photo. Check the value and save the entry.";
    }

    private Task CreateMileageEntryAsync(
        MileageEntrySourceDto source,
        string? sourceImagePath = null,
        string? initialNote = null)
    {
        OpenMileageDialog(source, sourceImagePath, initialNote);
        return Task.CompletedTask;
    }

    private void OpenMileageDialog(
        MileageEntrySourceDto source,
        string? sourceImagePath = null,
        string? initialNote = null,
        string? recognizedText = null,
        string? suggestedOdometerText = null)
    {
        DateTime now = DateTime.Now;
        ResetHomeDialogInputs(DateOnly.FromDateTime(now), TimeOnly.FromDateTime(now));
        HomeDialogTitle = source == MileageEntrySourceDto.PhotoOcr
            ? "Odometer photo"
            : "Mileage entry";
        HomeDialogSubtitle = "Save the odometer reading and trip details.";
        HomeDialogPrimaryText = "Save entry";
        MileageNoteText = initialNote ?? string.Empty;
        MileageOdometerText = suggestedOdometerText ?? string.Empty;
        pendingMileageSource = source;
        pendingMileageSourceImagePath = sourceImagePath;
        pendingMileageRecognizedText = recognizedText;
        HomeDialogKind = HomeActionDialogKind.Mileage;
    }

    private async Task SubmitHomeDialogAsync()
    {
        HomeDialogError = string.Empty;

        try
        {
            switch (HomeDialogKind)
            {
                case HomeActionDialogKind.Task:
                    await SubmitTaskDialogAsync();
                    break;
                case HomeActionDialogKind.Reminder:
                    await SubmitReminderDialogAsync();
                    break;
                case HomeActionDialogKind.Mileage:
                    await SubmitMileageDialogAsync();
                    break;
            }
        }
        catch (Exception exception)
        {
            HomeDialogError = exception.Message;
        }
    }

    private async Task SubmitTaskDialogAsync()
    {
        if (string.IsNullOrWhiteSpace(DialogTitleText))
        {
            HomeDialogError = "Give the task a title.";
            return;
        }

        DateOnly taskDate = DateOnly.FromDateTime(DialogDate);
        DateTimeOffset? dueAtUtc = TaskHasDueTime
            ? BuildLocalDateTimeAsUtc(DialogDate, DialogTime)
            : null;

        await taskRepository.SaveAsync(new TaskItem
        {
            Title = DialogTitleText.Trim(),
            Description = string.IsNullOrWhiteSpace(DialogDescriptionText) ? null : DialogDescriptionText.Trim(),
            Status = TaskItemStatusDto.Open,
            Priority = ParseTaskPriority(SelectedTaskPriority),
            PlannedFor = taskDate,
            DueAtUtc = dueAtUtc
        });

        await LoadTasksAsync();
        await calendarViewModel.RefreshAsync();
        HomeStatusText = "Task created.";
        CloseHomeDialog();
    }

    private async Task SubmitReminderDialogAsync()
    {
        if (string.IsNullOrWhiteSpace(DialogTitleText))
        {
            HomeDialogError = "Give the reminder a title.";
            return;
        }

        DateTimeOffset triggerAtUtc = BuildLocalDateTimeAsUtc(DialogDate, DialogTime);
        ReminderDto reminder = new(
            Guid.Empty,
            DialogTitleText.Trim(),
            string.IsNullOrWhiteSpace(DialogDescriptionText) ? null : DialogDescriptionText.Trim(),
            ReminderTriggerTypeDto.Time,
            ReminderStatusDto.Scheduled,
            ReminderShouldNotify,
            triggerAtUtc,
            null,
            null,
            null,
            null,
            null,
            null,
            default,
            null);

        await reminderRepository.SaveAsync(reminder);
        await calendarViewModel.RefreshAsync();
        HomeStatusText = "Reminder created.";
        CloseHomeDialog();
    }

    private async Task SubmitMileageDialogAsync()
    {
        if (!TryParseWholeNumber(MileageOdometerText, out double odometer) || odometer <= 0)
        {
            HomeDialogError = "Enter a whole-number odometer value, for example 123456.";
            return;
        }

        if (!TryParseOptionalDecimal(MileageTripText, out double tripKm))
        {
            HomeDialogError = "Enter a valid trip distance or leave it empty.";
            return;
        }

        CurrentLocationResult? currentLocationResult = null;

        if (MileageAttachCurrentLocation)
        {
            currentLocationResult = await userLocationService.TryGetCurrentLocationAsync();

            if (!currentLocationResult.IsAvailable || currentLocationResult.Coordinate == null)
            {
                HomeDialogError = currentLocationResult.ErrorMessage ?? "NOAH could not get the current location.";
                return;
            }
        }

        await mileageRepository.SaveAsync(new MileageEntry
        {
            RecordedAtUtc = BuildLocalDateTimeAsUtc(DialogDate, DialogTime),
            Odometer = odometer,
            TripKm = tripKm,
            Source = pendingMileageSource,
            SourceImagePath = pendingMileageSourceImagePath,
            RecognizedText = pendingMileageSource == MileageEntrySourceDto.PhotoOcr ? pendingMileageRecognizedText : null,
            CorrectedText = pendingMileageSource == MileageEntrySourceDto.PhotoOcr ? odometer.ToString("0", CultureInfo.InvariantCulture) : null,
            LocationLatitude = currentLocationResult?.Coordinate?.Latitude,
            LocationLongitude = currentLocationResult?.Coordinate?.Longitude,
            LocationAccuracyMeters = currentLocationResult?.Coordinate?.AccuracyMeters,
            Note = string.IsNullOrWhiteSpace(MileageNoteText) ? string.Empty : MileageNoteText.Trim()
        });

        await LoadMileageAsync();
        HomeStatusText = "Mileage entry saved.";
        CloseHomeDialog();
    }

    private void ResetHomeDialogInputs(DateOnly date, TimeOnly time)
    {
        DialogTitleText = string.Empty;
        DialogDescriptionText = string.Empty;
        MileageOdometerText = string.Empty;
        MileageTripText = string.Empty;
        MileageNoteText = string.Empty;
        MileageAttachCurrentLocation = false;
        HomeDialogError = string.Empty;
        DialogDate = date.ToDateTime(TimeOnly.MinValue);
        DialogTime = time.ToTimeSpan();
    }

    private void CloseHomeDialog()
    {
        HomeDialogKind = HomeActionDialogKind.None;
        HomeDialogError = string.Empty;
        pendingMileageSourceImagePath = null;
        pendingMileageRecognizedText = null;
        pendingMileageSource = MileageEntrySourceDto.Manual;
    }

    private static DateTimeOffset BuildLocalDateTimeAsUtc(DateTime date, TimeSpan time)
    {
        DateTime localDateTime = DateOnly.FromDateTime(date).ToDateTime(TimeOnly.FromTimeSpan(time));
        return new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime)).ToUniversalTime();
    }

    private static TaskPriorityDto ParseTaskPriority(string? priority)
    {
        return Enum.TryParse(priority, ignoreCase: true, out TaskPriorityDto parsedPriority)
            ? parsedPriority
            : TaskPriorityDto.Normal;
    }
    private async Task DebouncedSearchNotesAsync(string query)
    {
        searchCancellationTokenSource?.Cancel();
        searchCancellationTokenSource?.Dispose();

        CancellationTokenSource cancellationTokenSource = new();
        searchCancellationTokenSource = cancellationTokenSource;

        try
        {
            await Task.Delay(250, cancellationTokenSource.Token);
            await SearchNotesAsync(query, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            HomeStatusText = $"Search failed: {exception.Message}";
        }
    }

    private async Task SearchNotesAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            await LoadNotesAsync();
            return;
        }

        IReadOnlyList<Note> results = await noteRepository.SearchAsync(query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        PopulateNoteCollections(results);

        RefreshHomeCollectionState();
    }


    private static string? FormatWholeOdometerText(double? value)
    {
        return value.HasValue
            ? Math.Round(value.Value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            : null;
    }

    private static bool TryParseWholeNumber(string? value, out double result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmedValue = value.Trim();

        if (!long.TryParse(trimmedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedValue) &&
            !long.TryParse(trimmedValue, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsedValue))
        {
            return false;
        }

        result = parsedValue;
        return true;
    }
    private static bool TryParseDecimal(string? value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
               double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
    }

    private static bool TryParseOptionalDecimal(string? value, out double result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0;
            return true;
        }

        return TryParseDecimal(value, out result);
    }

    public void NavigateToSection(string? section)
    {
        switch (section?.Trim().ToLowerInvariant())
        {
            case "notes":
                NavigateNotes();
                break;
            case "calendar":
                NavigateCalendar();
                break;
            case "mileage":
                NavigateMileage();
                break;
            default:
                NavigateHome();
                break;
        }
    }
    private void ToggleChat()
    {
        IsChatOpen = !IsChatOpen;

        if (!IsChatOpen)
        {
            IsChatMenuOpen = false;
            IsSettingsOpen = false;
        }
    }

    private async Task NavigateChatAsync()
    {
        IsSettingsOpen = false;
        IsChatMenuOpen = false;
        IsChatOpen = false;

        await Shell.Current.GoToAsync("assistant");
    }

    private void NavigateHome()
    {
        IsNotesPageOpen = false;
        IsCalendarPageOpen = false;
        IsMileagePageOpen = false;
        IsNoteDetailOpen = false;
        IsChatOpen = false;
        IsChatMenuOpen = false;
        IsSettingsOpen = false;
    }

    private void NavigateNotes()
    {
        IsNotesPageOpen = true;
        IsCalendarPageOpen = false;
        IsMileagePageOpen = false;
        IsNoteDetailOpen = false;
        IsChatOpen = false;
        IsChatMenuOpen = false;
        IsSettingsOpen = false;

        if (SelectedNote == null && RecentNotes.Count > 0)
        {
            SelectedNote = RecentNotes[0];
        }
    }

    private void OpenNote(Note? note)
    {
        if (note == null)
        {
            return;
        }

        SelectedNote = note;
        NavigateNotes();
        IsNoteDetailOpen = true;
    }

    private void CloseNoteDetail()
    {
        noteSaveCancellationTokenSource?.Cancel();
        noteSaveCancellationTokenSource?.Dispose();
        noteSaveCancellationTokenSource = null;

        bool hasTypedDraftContent = !string.IsNullOrWhiteSpace(NoteEditorContent);
        bool isEmptyDraft =
            noteBeingEdited == null &&
            !hasTypedDraftContent &&
            string.IsNullOrWhiteSpace(NoteEditorTitle);

        if (hasTypedDraftContent)
        {
            _ = PersistNoteEditorAsync(CancellationToken.None);
        }

        if (isEmptyDraft)
        {
            NoteEditorStatusText = string.Empty;
        }

        IsNoteDetailOpen = false;
    }

    private void NavigateCalendar()
    {
        IsCalendarPageOpen = true;
        IsNotesPageOpen = false;
        IsMileagePageOpen = false;
        IsNoteDetailOpen = false;
        IsChatOpen = false;
        IsChatMenuOpen = false;
        IsSettingsOpen = false;
        _ = calendarViewModel.RefreshAsync();
    }

    private void NavigateMileage()
    {
        IsMileagePageOpen = true;
        IsNotesPageOpen = false;
        IsCalendarPageOpen = false;
        IsNoteDetailOpen = false;
        IsChatOpen = false;
        IsChatMenuOpen = false;
        IsSettingsOpen = false;
    }

    private Task SaveSettingsAsync()
    {
        AssistantClientSettings currentSettings = assistantApiSettingsService.Load();
        assistantApiSettingsService.Save(new AssistantClientSettings(
            ApiBaseUrl.Trim(),
            ApiKey.Trim(),
            currentSettings.LastSelectedChatId));
        IsSettingsOpen = false;
        return Task.CompletedTask;
    }

    private static string NormalizeResponseMode(string? responseMode)
    {
        return responseMode?.Trim() switch
        {
            "Speech" => "Speech",
            "Voice" => "Speech",
            "TextAndSpeech" => "TextAndSpeech",
            "Both" => "TextAndSpeech",
            _ => "Text"
        };
    }

    private static string NormalizeSpeechCulture(string? speechCulture)
    {
        string value = string.IsNullOrWhiteSpace(speechCulture) ? "English (en-EN)" : speechCulture.Trim();
        int open = value.LastIndexOf('(');
        int close = value.LastIndexOf(')');

        return open >= 0 && close > open
            ? value.Substring(open + 1, close - open - 1)
            : value;
    }

    private static string FormatSpeechCulture(string? speechCulture)
    {
        return NormalizeSpeechCulture(speechCulture) switch
        {
            "nl-NL" => "Dutch (nl-NL)",
            "en-US" => "English (en-US)",
            "en-GB" => "English (en-GB)",
            _ => "English (en-EN)"
        };
    }

    private async Task SendChatAsync()
    {
        string text = ChatInput.Trim();
        if (string.IsNullOrEmpty(text) || IsChatLoading)
        {
            return;
        }

        ChatMessages.Add(new ChatMessage
        {
            From = ChatMessage.Sender.User,
            Content = text,
            SentAt = DateTime.Now
        });

        ChatInput = string.Empty;
        IsChatLoading = true;

        try
        {
            string reply = await aiChatService.SendAsync(ChatMessages, text);

            ChatMessages.Add(new ChatMessage
            {
                From = ChatMessage.Sender.AI,
                Content = reply,
                SentAt = DateTime.Now
            });
        }
        catch (Exception exception)
        {
            ChatMessages.Add(new ChatMessage
            {
                From = ChatMessage.Sender.AI,
                Content = $"Sorry, something went wrong: {exception.Message}",
                SentAt = DateTime.Now
            });
        }
        finally
        {
            IsChatLoading = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
