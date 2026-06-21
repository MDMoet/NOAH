namespace Client.Models;

/// <summary>
/// Identifies the kind of local draft attachment shown in the assistant composer.
/// </summary>
public enum AssistantDraftAttachmentKind
{
    File = 0,
    Photo = 1
}

/// <summary>
/// Represents one local attachment staged in the assistant composer.
/// </summary>
public sealed class AssistantDraftAttachment : ObservableObject
{
    private string displayName = string.Empty;

    public Guid Id { get; init; } = Guid.NewGuid();

    public AssistantDraftAttachmentKind Kind { get; init; }

    public string FilePath { get; init; } = string.Empty;

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public string IconSource => Kind == AssistantDraftAttachmentKind.Photo
        ? "camera_outline_purple.png"
        : "paperclip_purple.png";

    public static AssistantDraftAttachment CreateFile(string filePath)
    {
        return new AssistantDraftAttachment
        {
            Kind = AssistantDraftAttachmentKind.File,
            FilePath = filePath,
            DisplayName = Path.GetFileName(filePath)
        };
    }

    public static AssistantDraftAttachment CreatePhoto(string filePath)
    {
        return new AssistantDraftAttachment
        {
            Kind = AssistantDraftAttachmentKind.Photo,
            FilePath = filePath,
            DisplayName = Path.GetFileName(filePath)
        };
    }
}
