using Client.Models;

namespace Client.UiTests;

public sealed class AssistantChatListItemUpdateTests
{
    [Fact]
    public void Update_RefreshesArchiveLabelAndMessageSummary()
    {
        AssistantChatListItem chat = new(new AssistantChatDto(
            Guid.NewGuid(),
            "Planner",
            "Initial",
            false,
            1,
            "Old preview",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1)));

        chat.Update(new AssistantChatDto(
            chat.Id,
            "Planner",
            "Initial",
            true,
            4,
            "Updated preview",
            DateTimeOffset.UtcNow,
            chat.CreatedAtUtc,
            DateTimeOffset.UtcNow));

        Assert.True(chat.IsArchived);
        Assert.Equal("Restore", chat.ArchiveActionLabel);
        Assert.Equal("Updated preview", chat.Subtitle);
    }
}
