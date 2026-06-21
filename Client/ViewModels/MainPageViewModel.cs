using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Client.Models;
using Client.Services;

namespace Client.ViewModels;

public sealed class MainPageViewModel : ObservableObject
{
    private const string MicrophoneIconSource = "microphone_outline_dark.png";
    private const string SendIconSource = "send_outline_dark.png";
    private const string StopIconSource = "square_outline_dark.png";
    private static readonly TimeSpan AssistantMessageTimeout = TimeSpan.FromMinutes(3);

    private readonly AssistantApiService assistantApiService;
    private readonly AssistantApiSettingsService settingsService;
    private readonly UserDialogService dialogService;
    private readonly ISpeechToTextService speechToTextService;
    private readonly IUserLocationService userLocationService;
    private readonly Command createChatCommand;
    private readonly Command saveSettingsCommand;
    private readonly Command selectTextResponseModeCommand;
    private readonly Command selectSpeechResponseModeCommand;
    private readonly Command selectTextAndSpeechResponseModeCommand;
    private readonly Command primaryActionCommand;
    private readonly Command attachFileCommand;
    private readonly Command capturePhotoAttachmentCommand;
    private readonly Command toggleSearchCommand;
    private readonly Command toggleArchivedChatsCommand;
    private readonly Command refreshSelectedChatCommand;
    private readonly Command navigateHomeCommand;
    private readonly Command navigateNotesCommand;
    private readonly Command navigateCalendarCommand;
    private readonly Command navigateMileageCommand;
    private readonly Command navigateChatCommand;
    private readonly Command<AssistantChatListItem> selectChatCommand;
    private readonly Command<AssistantChatListItem> showChatActionsCommand;
    private readonly Command<AssistantChatListItem> renameChatCommand;
    private readonly Command<AssistantChatListItem> saveChatRenameCommand;
    private readonly Command<AssistantChatListItem> cancelRenameChatCommand;
    private readonly Command<AssistantChatListItem> toggleArchiveChatCommand;
    private readonly Command<AssistantChatListItem> deleteChatCommand;
    private readonly Command<AssistantDraftAttachment> removeAttachmentCommand;
    private readonly List<AssistantChatListItem> allChats = [];

    private bool isInitialized;
    private bool isDrawerOpen;
    private bool isSettingsVisible;
    private bool isSearchVisible;
    private bool isLoadingChats;
    private bool isLoadingMessages;
    private bool isSendingMessage;
    private bool isListening;
    private bool showArchivedChats;
    private string apiBaseUrl = string.Empty;
    private string apiKey = string.Empty;
    private string searchText = string.Empty;
    private string composerText = string.Empty;
    private string statusText = string.Empty;
    private string preferredResponseMode = "Text";
    private string speechCulture = "English (en-EN)";
    private AssistantChatListItem? selectedChat;
    private CancellationTokenSource? activeSendCancellationTokenSource;
    private CancellationTokenSource? activeListeningCancellationTokenSource;
    private bool sendCancellationWasRequestedByUser;

    public MainPageViewModel(
        AssistantApiService assistantApiService,
        AssistantApiSettingsService settingsService,
        UserDialogService dialogService,
        ISpeechToTextService speechToTextService,
        IUserLocationService userLocationService)
    {
        this.assistantApiService = assistantApiService;
        this.settingsService = settingsService;
        this.dialogService = dialogService;
        this.speechToTextService = speechToTextService;
        this.userLocationService = userLocationService;

        ToggleDrawerCommand = new Command(ToggleDrawer);
        CloseDrawerCommand = new Command(CloseDrawer);
        navigateHomeCommand = new Command(async () => await NavigateToHomeSectionAsync(null));
        navigateNotesCommand = new Command(async () => await NavigateToHomeSectionAsync("notes"));
        navigateCalendarCommand = new Command(async () => await NavigateToHomeSectionAsync("calendar"));
        navigateMileageCommand = new Command(async () => await NavigateToHomeSectionAsync("mileage"));
        navigateChatCommand = new Command(StayOnChat);
        ToggleSettingsCommand = new Command(ToggleSettings);
        toggleSearchCommand = new Command(ToggleSearch);
        toggleArchivedChatsCommand = new Command(async () => await ToggleArchivedChatsAsync());
        createChatCommand = new Command(async () => await CreateChatAsync(), () => !IsLoadingChats);
        saveSettingsCommand = new Command(async () => await SaveSettingsAsync());
        selectTextResponseModeCommand = new Command(() => PreferredResponseMode = "Text");
        selectSpeechResponseModeCommand = new Command(() => PreferredResponseMode = "Speech");
        selectTextAndSpeechResponseModeCommand = new Command(() => PreferredResponseMode = "TextAndSpeech");
        primaryActionCommand = new Command(async () => await ExecutePrimaryActionAsync());
        attachFileCommand = new Command(async () => await AttachFileAsync());
        capturePhotoAttachmentCommand = new Command(async () => await CapturePhotoAttachmentAsync());
        refreshSelectedChatCommand = new Command(
            async () => await RefreshSelectedChatAsync(),
            () => !IsLoadingMessages);
        selectChatCommand = new Command<AssistantChatListItem>(async chat => await SelectChatAsync(chat));
        showChatActionsCommand = new Command<AssistantChatListItem>(async chat => await ShowChatActionsAsync(chat));
        renameChatCommand = new Command<AssistantChatListItem>(async chat => await RenameChatAsync(chat));
        saveChatRenameCommand = new Command<AssistantChatListItem>(async chat => await SaveChatRenameAsync(chat));
        cancelRenameChatCommand = new Command<AssistantChatListItem>(CancelRenameChat);
        toggleArchiveChatCommand = new Command<AssistantChatListItem>(async chat => await ToggleArchiveChatAsync(chat));
        deleteChatCommand = new Command<AssistantChatListItem>(async chat => await DeleteChatAsync(chat));
        removeAttachmentCommand = new Command<AssistantDraftAttachment>(RemoveAttachment);

        DraftAttachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasDraftAttachments));
        };
    }

    public ObservableCollection<AssistantChatListItem> Chats { get; } = [];

    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    public ObservableCollection<AssistantDraftAttachment> DraftAttachments { get; } = [];

    public ObservableCollection<string> SpeechCultureOptions { get; } =
    [
        "English (en-EN)",
        "Dutch (nl-NL)",
        "English (en-US)",
        "English (en-GB)"
    ];

    public ICommand ToggleDrawerCommand { get; }

    public ICommand CloseDrawerCommand { get; }

    public ICommand NavigateHomeCommand => navigateHomeCommand;

    public ICommand NavigateNotesCommand => navigateNotesCommand;

    public ICommand NavigateCalendarCommand => navigateCalendarCommand;

    public ICommand NavigateMileageCommand => navigateMileageCommand;

    public ICommand NavigateChatCommand => navigateChatCommand;
    public ICommand ToggleSettingsCommand { get; }

    public ICommand ToggleSearchCommand => toggleSearchCommand;

    public ICommand ToggleArchivedChatsCommand => toggleArchivedChatsCommand;

    public ICommand CreateChatCommand => createChatCommand;

    public ICommand SaveSettingsCommand => saveSettingsCommand;

    public ICommand SelectTextResponseModeCommand => selectTextResponseModeCommand;

    public ICommand SelectSpeechResponseModeCommand => selectSpeechResponseModeCommand;

    public ICommand SelectTextAndSpeechResponseModeCommand => selectTextAndSpeechResponseModeCommand;

    public ICommand PrimaryActionCommand => primaryActionCommand;

    public ICommand AttachFileCommand => attachFileCommand;

    public ICommand CapturePhotoAttachmentCommand => capturePhotoAttachmentCommand;

    public ICommand RefreshSelectedChatCommand => refreshSelectedChatCommand;

    public ICommand SelectChatCommand => selectChatCommand;

    public ICommand ShowChatActionsCommand => showChatActionsCommand;

    public ICommand RenameChatCommand => renameChatCommand;

    public ICommand SaveChatRenameCommand => saveChatRenameCommand;

    public ICommand CancelRenameChatCommand => cancelRenameChatCommand;

    public ICommand ToggleArchiveChatCommand => toggleArchiveChatCommand;

    public ICommand DeleteChatCommand => deleteChatCommand;

    public ICommand RemoveAttachmentCommand => removeAttachmentCommand;

    public string ApiBaseUrl
    {
        get => apiBaseUrl;
        set => SetProperty(ref apiBaseUrl, value);
    }

    public string ApiKey
    {
        get => apiKey;
        set => SetProperty(ref apiKey, value);
    }

    public string PreferredResponseMode
    {
        get => preferredResponseMode;
        set
        {
            if (SetProperty(ref preferredResponseMode, NormalizeResponseMode(value)))
            {
                OnPropertyChanged(nameof(IsTextResponseModeSelected));
                OnPropertyChanged(nameof(IsSpeechResponseModeSelected));
                OnPropertyChanged(nameof(IsTextAndSpeechResponseModeSelected));
            }
        }
    }

    public string SpeechCulture
    {
        get => speechCulture;
        set => SetProperty(ref speechCulture, string.IsNullOrWhiteSpace(value) ? "English (en-EN)" : value.Trim());
    }

    public bool IsTextResponseModeSelected => PreferredResponseMode == "Text";

    public bool IsSpeechResponseModeSelected => PreferredResponseMode == "Speech";

    public bool IsTextAndSpeechResponseModeSelected => PreferredResponseMode == "TextAndSpeech";

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                ApplyChatFilter();
            }
        }
    }

    public string ComposerText
    {
        get => composerText;
        set
        {
            if (SetProperty(ref composerText, value))
            {
                OnPropertyChanged(nameof(HasComposerText));
                OnPropertyChanged(nameof(CurrentPrimaryActionIcon));
            }
        }
    }

    public string StatusText
    {
        get => statusText;
        private set
        {
            if (SetProperty(ref statusText, value))
            {
                OnPropertyChanged(nameof(HasStatusText));
            }
        }
    }

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    public bool HasComposerText => !string.IsNullOrWhiteSpace(ComposerText);

    public string CurrentPrimaryActionIcon => IsSendingMessage
        ? StopIconSource
        : IsListening
        ? StopIconSource
        : HasComposerText
            ? SendIconSource
            : MicrophoneIconSource;

    public bool ShowArchivedChats
    {
        get => showArchivedChats;
        private set
        {
            if (SetProperty(ref showArchivedChats, value))
            {
                OnPropertyChanged(nameof(ChatListTitle));
                OnPropertyChanged(nameof(ArchivedChatsToggleText));
                OnPropertyChanged(nameof(ArchivedChatsToggleSubtitle));
                OnPropertyChanged(nameof(ChatEmptyStateText));
            }
        }
    }

    public string ChatListTitle => ShowArchivedChats
        ? "Archived chats"
        : "Chats";

    public string ArchivedChatsToggleText => ShowArchivedChats
        ? "Back to active chats"
        : "Show archived chats";

    public string ArchivedChatsToggleSubtitle => ShowArchivedChats
        ? "Return to your active conversations"
        : "Review and restore archived chats";

    public string ChatEmptyStateText => ShowArchivedChats
        ? "No archived chats yet."
        : "No chats yet.";

    public bool IsDrawerOpen
    {
        get => isDrawerOpen;
        set => SetProperty(ref isDrawerOpen, value);
    }

    public bool IsSettingsVisible
    {
        get => isSettingsVisible;
        set => SetProperty(ref isSettingsVisible, value);
    }

    public bool IsSearchVisible
    {
        get => isSearchVisible;
        set => SetProperty(ref isSearchVisible, value);
    }

    public bool IsLoadingChats
    {
        get => isLoadingChats;
        private set
        {
            if (SetProperty(ref isLoadingChats, value))
            {
                createChatCommand.ChangeCanExecute();
            }
        }
    }

    public bool IsLoadingMessages
    {
        get => isLoadingMessages;
        private set
        {
            if (SetProperty(ref isLoadingMessages, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                refreshSelectedChatCommand.ChangeCanExecute();
            }
        }
    }

    public bool IsSendingMessage
    {
        get => isSendingMessage;
        private set
        {
            if (SetProperty(ref isSendingMessage, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CurrentPrimaryActionIcon));
            }
        }
    }

    public bool IsListening
    {
        get => isListening;
        private set
        {
            if (SetProperty(ref isListening, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CurrentPrimaryActionIcon));
            }
        }
    }

    public bool IsBusy => IsLoadingMessages || IsSendingMessage || IsListening;

    public bool HasDraftAttachments => DraftAttachments.Count > 0;

    public AssistantChatListItem? SelectedChat
    {
        get => selectedChat;
        private set
        {
            if (SetProperty(ref selectedChat, value))
            {
                MarkSelectedChat(value?.Id);
            }
        }
    }

    public void PrepareForNavigation()
    {
        IsDrawerOpen = false;
        IsSettingsVisible = false;
        IsSearchVisible = false;
    }
    public async Task InitializeAsync()
    {
        if (isInitialized)
        {
            return;
        }

        isInitialized = true;

        AssistantClientSettings settings = settingsService.EnsureSeededDefaults();
        ApiBaseUrl = settings.ApiBaseUrl;
        ApiKey = settings.ApiKey;

        await LoadAssistantSettingsAsync();
        await RefreshChatsAsync(selectStoredChat: true, reloadSelectedChatMessages: true);
    }

    public void ToggleDrawer()
    {
        IsDrawerOpen = !IsDrawerOpen;
    }

    public void OpenDrawer()
    {
        IsDrawerOpen = true;
    }

    public void CloseDrawer()
    {
        IsDrawerOpen = false;
    }

    private async Task NavigateToHomeSectionAsync(string? section)
    {
        IsDrawerOpen = false;
        IsSettingsVisible = false;
        IsSearchVisible = false;

        string route = string.IsNullOrWhiteSpace(section)
            ? "//HomePage"
            : $"//HomePage?section={Uri.EscapeDataString(section)}";

        await Shell.Current.GoToAsync(route);
    }

    private void StayOnChat()
    {
        PrepareForNavigation();
    }
    public void ToggleSettings()
    {
        IsSettingsVisible = !IsSettingsVisible;

        if (IsSettingsVisible)
        {
            IsSearchVisible = false;
        }
    }

    public void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;

        if (IsSearchVisible)
        {
            IsSettingsVisible = false;
        }
        else if (!string.IsNullOrWhiteSpace(SearchText))
        {
            SearchText = string.Empty;
        }
    }

    private void PersistSettings(Guid? selectedChatId = null)
    {
        settingsService.Save(new AssistantClientSettings(
            ApiBaseUrl.Trim(),
            ApiKey.Trim(),
            selectedChatId ?? SelectedChat?.Id));
    }

    private async Task SaveSettingsAsync()
    {
        PersistSettings();
        StatusText = string.Empty;
        IsSettingsVisible = false;

        try
        {
            AssistantSettingsDto updatedSettings = await assistantApiService.UpdateAssistantSettingsAsync(
                new UpdateAssistantSettingsRequest(
                    PreferredResponseMode,
                    NormalizeSpeechCulture(SpeechCulture),
                    true,
                    true,
                    true,
                    8,
                    12));

            ApplyAssistantSettings(updatedSettings);
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
    }

    private async Task LoadAssistantSettingsAsync()
    {
        try
        {
            AssistantSettingsDto assistantSettings = await assistantApiService.GetAssistantSettingsAsync();
            ApplyAssistantSettings(assistantSettings);
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
    }

    private void ApplyAssistantSettings(AssistantSettingsDto assistantSettings)
    {
        PreferredResponseMode = assistantSettings.PreferredResponseMode;
        SpeechCulture = FormatSpeechCulture(assistantSettings.SpeechCulture);
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

    private async Task RefreshChatsAsync(
        bool selectStoredChat,
        bool reloadSelectedChatMessages)
    {
        if (IsLoadingChats)
        {
            return;
        }

        try
        {
            IsLoadingChats = true;
            PersistSettings();

            IReadOnlyList<AssistantChatDto> chatDtos = await assistantApiService.GetChatsAsync();
            Guid? preferredChatId = SelectedChat?.Id;

            if (selectStoredChat && !preferredChatId.HasValue)
            {
                preferredChatId = settingsService.Load().LastSelectedChatId;
            }

            allChats.Clear();
            allChats.AddRange(chatDtos
                .OrderByDescending(chat => chat.LastMessageAtUtc ?? chat.CreatedAtUtc)
                .Select(chat => new AssistantChatListItem(chat)));

            ApplyChatFilter();

            AssistantChatListItem? preferredChat = FindPreferredChat(preferredChatId);

            if (preferredChat == null)
            {
                SelectedChat = null;
                Messages.Clear();
                StatusText = string.Empty;
                settingsService.SaveSelectedChatId(null);
                return;
            }

            bool selectedChatChanged = SelectedChat?.Id != preferredChat.Id;
            SelectedChat = preferredChat;
            settingsService.SaveSelectedChatId(preferredChat.Id);

            if (reloadSelectedChatMessages || selectedChatChanged)
            {
                await LoadSelectedChatMessagesAsync(preferredChat.Id);
            }
            else
            {
                StatusText = string.Empty;
            }
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsLoadingChats = false;
        }
    }

    private void ApplyChatFilter()
    {
        Guid? selectedChatId = SelectedChat?.Id;

        Chats.Clear();

        foreach (AssistantChatListItem chat in GetChatsForCurrentView(applySearchFilter: true))
        {
            chat.IsSelected = selectedChatId.HasValue && chat.Id == selectedChatId.Value;
            Chats.Add(chat);
        }
    }

    private void MarkSelectedChat(Guid? chatId)
    {
        foreach (AssistantChatListItem chat in allChats)
        {
            chat.IsSelected = chatId.HasValue && chat.Id == chatId.Value;
        }
    }

    private void CloseAllChatMenus(Guid? exceptChatId = null)
    {
        foreach (AssistantChatListItem chat in allChats)
        {
            if (exceptChatId.HasValue && chat.Id == exceptChatId.Value)
            {
                continue;
            }

            chat.CloseActions();
        }
    }

    private Task CreateChatAsync()
    {
        if (IsLoadingChats)
        {
            return Task.CompletedTask;
        }

        CloseAllChatMenus();

        // Starting a new chat should behave like opening a fresh draft.
        // The backend chat is created only once the first message is actually sent.
        ShowArchivedChats = false;
        SelectedChat = null;
        Messages.Clear();
        StatusText = string.Empty;
        SearchText = string.Empty;
        IsSearchVisible = false;
        IsSettingsVisible = false;
        IsDrawerOpen = false;
        PersistSettings(selectedChatId: null);
        settingsService.SaveSelectedChatId(null);
        ApplyChatFilter();
        return Task.CompletedTask;
    }

    private async Task ToggleArchivedChatsAsync()
    {
        if (IsLoadingChats || IsLoadingMessages)
        {
            return;
        }

        CloseAllChatMenus();
        ShowArchivedChats = !ShowArchivedChats;
        ApplyChatFilter();

        AssistantChatListItem? preferredChat = FindPreferredChat(SelectedChat?.Id);

        if (preferredChat == null)
        {
            SelectedChat = null;
            Messages.Clear();
            StatusText = string.Empty;
            settingsService.SaveSelectedChatId(null);
            return;
        }

        if (SelectedChat?.Id == preferredChat.Id)
        {
            StatusText = string.Empty;
            return;
        }

        SelectedChat = preferredChat;
        settingsService.SaveSelectedChatId(preferredChat.Id);
        await LoadSelectedChatMessagesAsync(preferredChat.Id);
    }

    private async Task SelectChatAsync(AssistantChatListItem? chat)
    {
        if (chat == null)
        {
            return;
        }

        CloseAllChatMenus();
        SelectedChat = chat;
        settingsService.SaveSelectedChatId(chat.Id);
        IsDrawerOpen = false;
        await LoadSelectedChatMessagesAsync(chat.Id);
    }

    private async Task LoadSelectedChatMessagesAsync(Guid chatId)
    {
        try
        {
            IsLoadingMessages = true;
            PersistSettings(chatId);

            IReadOnlyList<AssistantInteractionDto> interactions =
                await assistantApiService.GetChatMessagesAsync(chatId, 200);

            Messages.Clear();

            foreach (ChatMessageItem message in MapMessages(interactions))
            {
                Messages.Add(message);
            }

            StatusText = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsLoadingMessages = false;
        }
    }

    private Task RenameChatAsync(AssistantChatListItem? chat)
    {
        if (chat == null)
        {
            return Task.CompletedTask;
        }

        CloseAllChatMenus(chat.Id);
        chat.BeginRename();
        StatusText = string.Empty;
        return Task.CompletedTask;
    }

    private async Task SaveChatRenameAsync(AssistantChatListItem? chat)
    {
        if (chat == null)
        {
            return;
        }

        string updatedTitle = chat.EditableTitle.Trim();
        if (string.IsNullOrWhiteSpace(updatedTitle))
        {
            StatusText = "Give the chat a title.";
            return;
        }

        if (string.Equals(updatedTitle, chat.Title, StringComparison.Ordinal))
        {
            chat.CloseActions();
            StatusText = string.Empty;
            return;
        }

        try
        {
            AssistantChatDto updatedChat = await assistantApiService.UpdateChatAsync(
                chat.Id,
                new UpdateAssistantChatRequest(updatedTitle, null, null));
            chat.Update(updatedChat);
            chat.CloseActions();
            ApplyChatFilter();
            StatusText = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
    }

    private void CancelRenameChat(AssistantChatListItem? chat)
    {
        chat?.CloseActions();
        StatusText = string.Empty;
    }

    private Task ShowChatActionsAsync(AssistantChatListItem? chat)
    {
        if (chat == null)
        {
            return Task.CompletedTask;
        }

        if (chat.IsMenuOpen)
        {
            chat.CloseActions();
            return Task.CompletedTask;
        }

        CloseAllChatMenus(chat.Id);
        chat.OpenActions();
        StatusText = string.Empty;
        return Task.CompletedTask;
    }

    private async Task ToggleArchiveChatAsync(AssistantChatListItem? chat)
    {
        if (chat == null)
        {
            return;
        }

        bool shouldArchive = !chat.IsArchived;

        try
        {
            CloseAllChatMenus();
            await assistantApiService.UpdateChatAsync(
                chat.Id,
                new UpdateAssistantChatRequest(null, null, shouldArchive));
            await RefreshChatsAsync(selectStoredChat: true, reloadSelectedChatMessages: SelectedChat?.Id == chat.Id);
            StatusText = shouldArchive
                ? "Chat archived."
                : "Chat restored.";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
    }

    private async Task DeleteChatAsync(AssistantChatListItem? chat)
    {
        if (chat == null)
        {
            return;
        }

        bool confirmed = await dialogService.ConfirmAsync(
            "Delete chat",
            "Delete this chat and its messages? This cannot be undone.",
            "Delete",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        try
        {
            CloseAllChatMenus();
            await assistantApiService.DeleteChatAsync(chat.Id);
            await RefreshChatsAsync(selectStoredChat: true, reloadSelectedChatMessages: SelectedChat?.Id == chat.Id);
            StatusText = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
    }

    private static IReadOnlyList<ChatMessageItem> MapMessages(
        IReadOnlyList<AssistantInteractionDto> interactions)
    {
        List<ChatMessageItem> messages = [];

        foreach (AssistantInteractionDto interaction in interactions.OrderBy(item => item.RequestedAtUtc))
        {
            messages.Add(ChatMessageItem.CreateUser(interaction.UserInput, interaction.RequestedAtUtc));

            TimeSpan? elapsed = interaction.CompletedAtUtc.HasValue
                ? interaction.CompletedAtUtc.Value - interaction.RequestedAtUtc
                : null;
            string? metaText = elapsed.HasValue && elapsed.Value.TotalMilliseconds > 0
                ? $"Thought for {FormatElapsed(elapsed.Value)}"
                : null;

            if (!string.IsNullOrWhiteSpace(interaction.AssistantResponse))
            {
                messages.Add(ChatMessageItem.CreateAssistant(
                    interaction.AssistantResponse,
                    interaction.CompletedAtUtc ?? interaction.RequestedAtUtc,
                    metaText,
                    string.Equals(interaction.Status, "Failed", StringComparison.OrdinalIgnoreCase)));
            }
            else if (!string.IsNullOrWhiteSpace(interaction.ErrorMessage))
            {
                messages.Add(ChatMessageItem.CreateAssistant(
                    interaction.ErrorMessage,
                    interaction.CompletedAtUtc ?? interaction.RequestedAtUtc,
                    metaText,
                    isError: true));
            }
        }

        return messages;
    }

    private async Task ExecutePrimaryActionAsync()
    {
        if (IsSendingMessage)
        {
            CancelSend();
            return;
        }

        if (IsListening)
        {
            CancelListening();
            return;
        }

        if (!HasComposerText)
        {
            await ListenAndSendAsync();
            return;
        }

        await SendMessageAsync();
    }

    private void CancelSend()
    {
        if (activeSendCancellationTokenSource == null || !IsSendingMessage)
        {
            return;
        }

        sendCancellationWasRequestedByUser = true;
        activeSendCancellationTokenSource.Cancel();
    }

    private void CancelListening()
    {
        activeListeningCancellationTokenSource?.Cancel();
    }

    private async Task ListenAndSendAsync()
    {
        if (IsListening || IsSendingMessage)
        {
            return;
        }

        if (!speechToTextService.IsSupported)
        {
            StatusText = "Speech recognition is not supported on this device.";
            return;
        }

        try
        {
            IsListening = true;
            StatusText = "Listening...";
            activeListeningCancellationTokenSource = new CancellationTokenSource();

            SpeechRecognitionResult result = await speechToTextService.ListenOnceAsync(
                NormalizeSpeechCulture(SpeechCulture),
                activeListeningCancellationTokenSource.Token);

            if (result.WasCancelled)
            {
                StatusText = string.Empty;
                return;
            }

            if (!result.IsSuccessful || string.IsNullOrWhiteSpace(result.Text))
            {
                StatusText = result.ErrorMessage ?? "NOAH could not hear anything.";
                return;
            }

            ComposerText = result.Text.Trim();
            StatusText = string.Empty;
            await SendMessageAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            activeListeningCancellationTokenSource?.Dispose();
            activeListeningCancellationTokenSource = null;
            IsListening = false;
        }
    }

    private async Task SendMessageAsync()
    {
        if (IsSendingMessage || !HasComposerText)
        {
            return;
        }

        string messageText = ComposerText.Trim();
        ChatMessageItem? assistantMessage = null;
        Task? progressTask = null;
        CancellationTokenSource? progressCancellationTokenSource = null;
        Stopwatch? stopwatch = null;

        try
        {
            IsSendingMessage = true;
            sendCancellationWasRequestedByUser = false;
            PersistSettings();
            StatusText = string.Empty;
            activeSendCancellationTokenSource = new CancellationTokenSource();

            AssistantChatListItem activeChat =
                SelectedChat ?? await CreateChatForMessageAsync(activeSendCancellationTokenSource.Token);
            DateTimeOffset requestedAtUtc = DateTimeOffset.UtcNow;
            GeoCoordinateDto? currentLocation = await ResolveCurrentLocationForMessageAsync(
                messageText,
                activeSendCancellationTokenSource.Token);

            ComposerText = string.Empty;
            DraftAttachments.Clear();

            ChatMessageItem userMessage = ChatMessageItem.CreateUser(messageText, requestedAtUtc);
            assistantMessage = ChatMessageItem.CreatePendingAssistant(requestedAtUtc);
            assistantMessage.MetaText = "Thinking for <1s";
            Messages.Add(userMessage);
            Messages.Add(assistantMessage);

            stopwatch = Stopwatch.StartNew();
            progressCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(activeSendCancellationTokenSource.Token);
            progressTask = TrackAssistantProgressAsync(
                assistantMessage,
                stopwatch,
                progressCancellationTokenSource.Token);

            AssistantCommandResponse response = await assistantApiService.SendChatMessageAsync(
                activeChat.Id,
                new AssistantCommandRequest(
                    messageText,
                    "Text",
                    PreferredResponseMode,
                    currentLocation,
                    requestedAtUtc,
                    null),
                activeSendCancellationTokenSource.Token);

            stopwatch.Stop();
            progressCancellationTokenSource.Cancel();
            await AwaitProgressLoopAsync(progressTask);

            assistantMessage.Text = string.IsNullOrWhiteSpace(response.ResponseText)
                ? "NOAH did not return a response."
                : response.ResponseText.Trim();
            assistantMessage.IsPending = false;
            assistantMessage.IsError = string.Equals(response.Status, "Failed", StringComparison.OrdinalIgnoreCase);
            assistantMessage.MetaText = $"Thought for {FormatElapsed(stopwatch.Elapsed)}";

            await RefreshChatsAsync(selectStoredChat: true, reloadSelectedChatMessages: false);
        }
        catch (OperationCanceledException) when (sendCancellationWasRequestedByUser)
        {
            if (assistantMessage == null)
            {
                assistantMessage = ChatMessageItem.CreatePendingAssistant(DateTimeOffset.UtcNow);
                Messages.Add(assistantMessage);
            }

            assistantMessage.Text = "Cancelled.";
            assistantMessage.IsPending = false;
            assistantMessage.IsError = true;
            assistantMessage.MetaText = "Cancelled";
            StatusText = string.Empty;
        }
        catch (OperationCanceledException)
        {
            if (assistantMessage == null)
            {
                assistantMessage = ChatMessageItem.CreatePendingAssistant(DateTimeOffset.UtcNow);
                Messages.Add(assistantMessage);
            }

            assistantMessage.Text = "NOAH took too long to respond.";
            assistantMessage.IsPending = false;
            assistantMessage.IsError = true;
            assistantMessage.MetaText = "Timed out";
            StatusText = $"No response after {FormatElapsed(AssistantMessageTimeout)}.";
        }
        catch (Exception exception)
        {
            assistantMessage ??= Messages.LastOrDefault(message => message.IsPending) ??
                                 ChatMessageItem.CreatePendingAssistant(DateTimeOffset.UtcNow);

            if (!Messages.Contains(assistantMessage))
            {
                Messages.Add(assistantMessage);
            }

            assistantMessage.Text = exception.Message;
            assistantMessage.IsPending = false;
            assistantMessage.IsError = true;
            assistantMessage.MetaText = "Couldn't finish that";
            StatusText = exception.Message;
        }
        finally
        {
            progressCancellationTokenSource?.Cancel();
            await AwaitProgressLoopAsync(progressTask);
            progressCancellationTokenSource?.Dispose();
            activeSendCancellationTokenSource?.Dispose();
            activeSendCancellationTokenSource = null;
            sendCancellationWasRequestedByUser = false;
            IsSendingMessage = false;
        }
    }

    private async Task AttachFileAsync()
    {
        try
        {
            FileResult? file = await FilePicker.Default.PickAsync();

            if (file == null)
            {
                return;
            }

            AddOrReplaceDraftAttachment(AssistantDraftAttachment.CreateFile(file.FullPath));
            StatusText = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText = $"Attachment failed: {exception.Message}";
        }
    }

    private async Task CapturePhotoAttachmentAsync()
    {
        try
        {
            PermissionStatus permissionStatus = await Permissions.RequestAsync<Permissions.Camera>();

            if (permissionStatus != PermissionStatus.Granted)
            {
                StatusText = "Camera access was denied.";
                return;
            }

            if (!MediaPicker.Default.IsCaptureSupported)
            {
                StatusText = "Camera capture is not supported on this device.";
                return;
            }

            FileResult? photo = await MediaPicker.Default.CapturePhotoAsync();

            if (photo == null)
            {
                return;
            }

            AddOrReplaceDraftAttachment(AssistantDraftAttachment.CreatePhoto(photo.FullPath));
            StatusText = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText = $"Camera failed: {exception.Message}";
        }
    }

    private void AddOrReplaceDraftAttachment(AssistantDraftAttachment attachment)
    {
        AssistantDraftAttachment? existingAttachment = DraftAttachments
            .FirstOrDefault(item => string.Equals(item.FilePath, attachment.FilePath, StringComparison.OrdinalIgnoreCase));

        if (existingAttachment != null)
        {
            DraftAttachments.Remove(existingAttachment);
        }

        DraftAttachments.Add(attachment);
    }

    private void RemoveAttachment(AssistantDraftAttachment? attachment)
    {
        if (attachment == null)
        {
            return;
        }

        DraftAttachments.Remove(attachment);
    }

    private async Task<GeoCoordinateDto?> ResolveCurrentLocationForMessageAsync(
        string messageText,
        CancellationToken cancellationToken)
    {
        if (!LocationIntentDetector.RequiresCurrentLocation(messageText))
        {
            return null;
        }

        bool confirmed = await dialogService.ConfirmAsync(
            "Share location",
            "This request probably needs your current location. Share it with NOAH for this message?",
            "Share",
            "Not now");

        if (!confirmed)
        {
            return null;
        }

        CurrentLocationResult locationResult = await userLocationService.TryGetCurrentLocationAsync(cancellationToken);

        if (!locationResult.IsAvailable)
        {
            StatusText = locationResult.ErrorMessage ?? "NOAH could not get the current location.";
            return null;
        }

        return locationResult.Coordinate;
    }

    private async Task<AssistantChatListItem> CreateChatForMessageAsync(CancellationToken cancellationToken)
    {
        ShowArchivedChats = false;
        AssistantChatDto createdChat = await assistantApiService.CreateChatAsync(
            new CreateAssistantChatRequest(null, null),
            cancellationToken);
        AssistantChatListItem chatItem = new(createdChat);
        allChats.Insert(0, chatItem);
        ApplyChatFilter();
        SelectedChat = chatItem;
        settingsService.SaveSelectedChatId(chatItem.Id);
        return chatItem;
    }

    private static async Task TrackAssistantProgressAsync(
        ChatMessageItem assistantMessage,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            assistantMessage.MetaText = $"Thinking for {FormatElapsed(stopwatch.Elapsed)}";
            await Task.Delay(1000, cancellationToken);
        }
    }

    private static async Task AwaitProgressLoopAsync(Task? progressTask)
    {
        if (progressTask == null)
        {
            return;
        }

        try
        {
            await progressTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshSelectedChatAsync()
    {
        if (SelectedChat == null)
        {
            await RefreshChatsAsync(selectStoredChat: true, reloadSelectedChatMessages: true);
            return;
        }

        await LoadSelectedChatMessagesAsync(SelectedChat.Id);
        await RefreshChatsAsync(selectStoredChat: true, reloadSelectedChatMessages: false);
    }

    private IEnumerable<AssistantChatListItem> GetChatsForCurrentView(bool applySearchFilter)
    {
        IEnumerable<AssistantChatListItem> filteredChats = allChats
            .Where(chat => chat.IsArchived == ShowArchivedChats);

        string normalizedQuery = SearchText.Trim();

        if (!applySearchFilter || string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return filteredChats;
        }

        return filteredChats.Where(chat =>
            chat.Title.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(chat.Description) &&
             chat.Description.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(chat.LastMessagePreview) &&
             chat.LastMessagePreview.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)));
    }

    private AssistantChatListItem? FindPreferredChat(Guid? preferredChatId)
    {
        List<AssistantChatListItem> chatsForCurrentView = GetChatsForCurrentView(applySearchFilter: false).ToList();

        if (preferredChatId.HasValue)
        {
            AssistantChatListItem? preferredChat = chatsForCurrentView
                .FirstOrDefault(chat => chat.Id == preferredChatId.Value);

            if (preferredChat != null)
            {
                return preferredChat;
            }
        }

        return chatsForCurrentView.FirstOrDefault();
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
        {
            return "<1s";
        }

        if (elapsed.TotalSeconds < 10)
        {
            return $"{elapsed.TotalSeconds:0.#}s";
        }

        return $"{Math.Round(elapsed.TotalSeconds):0}s";
    }
}
