using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace Client.Models;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record AssistantSettingsDto(
    Guid Id,
    string PreferredResponseMode,
    string SpeechCulture,
    bool EnableChatMemory,
    bool EnableLongTermMemory,
    bool EnableMemoryCapture,
    int ConversationMemoryMessageCount,
    int LongTermMemoryItemCount);

public sealed record UpdateAssistantSettingsRequest(
    string PreferredResponseMode,
    string SpeechCulture,
    bool EnableChatMemory,
    bool EnableLongTermMemory,
    bool EnableMemoryCapture,
    int ConversationMemoryMessageCount,
    int LongTermMemoryItemCount);

public sealed record GeoCoordinateDto(
    double Latitude,
    double Longitude,
    double? AccuracyMeters);

public sealed record AssistantCommandRequest(
    string Input,
    string InputMode,
    string? PreferredResponseMode,
    GeoCoordinateDto? CurrentLocation,
    DateTimeOffset RequestedAtUtc,
    Guid? ChatId);

public sealed record AssistantCommandResponse(
    Guid InteractionId,
    Guid? ChatId,
    string ActionType,
    string Status,
    string ResponseText,
    string ResponseMode,
    Guid? RelatedEntityId,
    string? RelatedEntityType);

public sealed record AssistantInteractionDto(
    Guid Id,
    Guid? ChatId,
    string UserInput,
    string InputMode,
    string ActionType,
    string? AssistantResponse,
    string ResponseMode,
    string Status,
    Guid? RelatedEntityId,
    string? RelatedEntityType,
    string? ErrorMessage,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record AssistantChatDto(
    Guid Id,
    string Title,
    string? Description,
    bool IsArchived,
    int MessageCount,
    string? LastMessagePreview,
    DateTimeOffset? LastMessageAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateAssistantChatRequest(
    string? Title,
    string? Description);

public sealed record UpdateAssistantChatRequest(
    string? Title,
    string? Description,
    bool? IsArchived);

public sealed class AssistantChatListItem(AssistantChatDto chat) : ObservableObject
{
    private bool isSelected;
    private bool isMenuOpen;
    private bool isRenaming;
    private string editableTitle = chat.Title;

    public Guid Id { get; } = chat.Id;

    public string Title { get; private set; } = chat.Title;

    public string? Description { get; private set; } = chat.Description;

    public bool IsArchived { get; private set; } = chat.IsArchived;

    public int MessageCount { get; private set; } = chat.MessageCount;

    public string? LastMessagePreview { get; private set; } = chat.LastMessagePreview;

    public DateTimeOffset? LastMessageAtUtc { get; private set; } = chat.LastMessageAtUtc;

    public DateTimeOffset CreatedAtUtc { get; private set; } = chat.CreatedAtUtc;

    public DateTimeOffset? UpdatedAtUtc { get; private set; } = chat.UpdatedAtUtc;

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public bool IsMenuOpen
    {
        get => isMenuOpen;
        set
        {
            if (SetProperty(ref isMenuOpen, value))
            {
                OnPropertyChanged(nameof(IsActionMenuVisible));
                OnPropertyChanged(nameof(IsRenameEditorVisible));
            }
        }
    }

    public bool IsRenaming
    {
        get => isRenaming;
        set
        {
            if (SetProperty(ref isRenaming, value))
            {
                OnPropertyChanged(nameof(IsActionMenuVisible));
                OnPropertyChanged(nameof(IsRenameEditorVisible));
            }
        }
    }

    public string EditableTitle
    {
        get => editableTitle;
        set => SetProperty(ref editableTitle, value);
    }

    public bool IsActionMenuVisible => IsMenuOpen && !IsRenaming;

    public bool IsRenameEditorVisible => IsMenuOpen && IsRenaming;

    public string Subtitle =>
        !string.IsNullOrWhiteSpace(LastMessagePreview)
            ? LastMessagePreview!
            : MessageCount switch
            {
                0 => "No messages yet",
                1 => "1 message",
                _ => $"{MessageCount} messages"
            };

    public string ArchiveActionText => IsArchived
        ? "Restore chat"
        : "Archive chat";

    public string ArchiveActionLabel => IsArchived
        ? "Restore"
        : "Archive";

    public void OpenActions()
    {
        EditableTitle = Title;
        IsRenaming = false;
        IsMenuOpen = true;
    }

    public void BeginRename()
    {
        EditableTitle = Title;
        IsMenuOpen = true;
        IsRenaming = true;
    }

    public void CloseActions()
    {
        EditableTitle = Title;
        IsRenaming = false;
        IsMenuOpen = false;
    }

    public void Update(AssistantChatDto chatDto)
    {
        Title = chatDto.Title;
        Description = chatDto.Description;
        IsArchived = chatDto.IsArchived;
        MessageCount = chatDto.MessageCount;
        LastMessagePreview = chatDto.LastMessagePreview;
        LastMessageAtUtc = chatDto.LastMessageAtUtc;
        CreatedAtUtc = chatDto.CreatedAtUtc;
        UpdatedAtUtc = chatDto.UpdatedAtUtc;
        EditableTitle = chatDto.Title;
        IsRenaming = false;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsArchived));
        OnPropertyChanged(nameof(MessageCount));
        OnPropertyChanged(nameof(LastMessagePreview));
        OnPropertyChanged(nameof(LastMessageAtUtc));
        OnPropertyChanged(nameof(CreatedAtUtc));
        OnPropertyChanged(nameof(UpdatedAtUtc));
        OnPropertyChanged(nameof(EditableTitle));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ArchiveActionText));
        OnPropertyChanged(nameof(ArchiveActionLabel));
        OnPropertyChanged(nameof(IsActionMenuVisible));
        OnPropertyChanged(nameof(IsRenameEditorVisible));
    }
}

public sealed class ChatMessageItem : ObservableObject
{
    private string text = string.Empty;
    private string? metaText;
    private bool isPending;
    private bool isError;

    private ChatMessageItem()
    {
    }

    public Guid Id { get; init; } = Guid.NewGuid();

    public bool IsFromUser { get; init; }

    public bool IsFromAssistant => !IsFromUser;

    public DateTimeOffset Timestamp { get; init; }

    public string Text
    {
        get => text;
        set => SetProperty(ref text, value);
    }

    public string? MetaText
    {
        get => metaText;
        set
        {
            if (SetProperty(ref metaText, value))
            {
                OnPropertyChanged(nameof(HasMetaText));
            }
        }
    }

    public bool HasMetaText => !string.IsNullOrWhiteSpace(MetaText);

    public string TimestampText => Timestamp.ToLocalTime().ToString("HH:mm");

    public bool IsPending
    {
        get => isPending;
        set => SetProperty(ref isPending, value);
    }

    public bool IsError
    {
        get => isError;
        set => SetProperty(ref isError, value);
    }

    public static ChatMessageItem CreateUser(string text, DateTimeOffset timestamp)
    {
        return new ChatMessageItem
        {
            IsFromUser = true,
            Text = text,
            Timestamp = timestamp
        };
    }

    public static ChatMessageItem CreateAssistant(
        string text,
        DateTimeOffset timestamp,
        string? metaText = null,
        bool isError = false)
    {
        return new ChatMessageItem
        {
            IsFromUser = false,
            Text = text,
            Timestamp = timestamp,
            MetaText = metaText,
            IsError = isError,
            IsPending = false
        };
    }

    public static ChatMessageItem CreatePendingAssistant(DateTimeOffset timestamp)
    {
        return new ChatMessageItem
        {
            IsFromUser = false,
            Text = "Thinking...",
            Timestamp = timestamp,
            IsPending = true,
            IsError = false,
            MetaText = null
        };
    }
}
