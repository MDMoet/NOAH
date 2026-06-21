using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Client.Models;

public class Note : INotifyPropertyChanged
{
    private string title = string.Empty;
    private string content = string.Empty;
    private bool capturedFromVoice;
    private Guid? sourceInteractionId;
    private DateTimeOffset createdAtUtc;
    private DateTimeOffset? updatedAtUtc;
    private bool isSelected;

    public Guid Id { get; set; }

    public string Title
    {
        get => title;
        set
        {
            if (title == value)
            {
                return;
            }

            title = value;
            OnPropertyChanged();
        }
    }

    public string Content
    {
        get => content;
        set
        {
            if (content == value)
            {
                return;
            }

            content = value;
            OnPropertyChanged();
        }
    }

    public bool CapturedFromVoice
    {
        get => capturedFromVoice;
        set
        {
            if (capturedFromVoice == value)
            {
                return;
            }

            capturedFromVoice = value;
            OnPropertyChanged();
        }
    }

    public Guid? SourceInteractionId
    {
        get => sourceInteractionId;
        set
        {
            if (sourceInteractionId == value)
            {
                return;
            }

            sourceInteractionId = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset CreatedAtUtc
    {
        get => createdAtUtc;
        set
        {
            if (createdAtUtc == value)
            {
                return;
            }

            createdAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SortTimestamp));
        }
    }

    public DateTimeOffset? UpdatedAtUtc
    {
        get => updatedAtUtc;
        set
        {
            if (updatedAtUtc == value)
            {
                return;
            }

            updatedAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SortTimestamp));
        }
    }

    public DateTimeOffset SortTimestamp => UpdatedAtUtc ?? CreatedAtUtc;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
