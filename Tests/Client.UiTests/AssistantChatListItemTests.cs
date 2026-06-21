using Client.Models;

namespace Client.UiTests;

public sealed class AssistantChatListItemTests
{
    [Fact]
    public void BeginRename_ShowsInlineRenameEditor()
    {
        AssistantChatListItem chat = new(new AssistantChatDto(
            Guid.NewGuid(),
            "Road trip",
            null,
            false,
            2,
            "Latest",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1)));

        chat.BeginRename();

        Assert.True(chat.IsMenuOpen);
        Assert.True(chat.IsRenaming);
        Assert.True(chat.IsRenameEditorVisible);
        Assert.False(chat.IsActionMenuVisible);
    }

    [Fact]
    public void CloseActions_HidesAllInlineChatActions()
    {
        AssistantChatListItem chat = new(new AssistantChatDto(
            Guid.NewGuid(),
            "Ideas",
            null,
            false,
            0,
            null,
            null,
            DateTimeOffset.UtcNow,
            null));

        chat.OpenActions();
        chat.CloseActions();

        Assert.False(chat.IsMenuOpen);
        Assert.False(chat.IsRenaming);
        Assert.False(chat.IsActionMenuVisible);
        Assert.False(chat.IsRenameEditorVisible);
    }
}
