using System.Globalization;
using Client.Models;

namespace Client.UiTests;

public sealed class ChatMessageItemTests
{
    [Fact]
    public void TimestampText_ForToday_ShowsOnlyTime()
    {
        DateTime localTime = DateTime.Today.AddHours(14).AddMinutes(5);
        DateTimeOffset timestamp = CreateLocalTimestamp(localTime);

        ChatMessageItem message = ChatMessageItem.CreateUser("hello", timestamp);

        Assert.Equal(localTime.ToString("HH:mm", CultureInfo.CurrentCulture), message.TimestampText);
    }

    [Fact]
    public void TimestampText_ForYesterday_ShowsYesterdayAndTime()
    {
        DateTime localTime = DateTime.Today.AddDays(-1).AddHours(9).AddMinutes(30);
        DateTimeOffset timestamp = CreateLocalTimestamp(localTime);

        ChatMessageItem message = ChatMessageItem.CreateAssistant("hello", timestamp);

        Assert.Equal($"Yesterday {localTime.ToString("HH:mm", CultureInfo.CurrentCulture)}", message.TimestampText);
    }

    [Fact]
    public void TimestampText_ForOlderMessages_ShowsDateAndTime()
    {
        DateTime localTime = DateTime.Today.AddDays(-2).AddHours(8).AddMinutes(15);
        DateTimeOffset timestamp = CreateLocalTimestamp(localTime);

        ChatMessageItem message = ChatMessageItem.CreateUser("hello", timestamp);

        Assert.Equal(localTime.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture), message.TimestampText);
    }

    private static DateTimeOffset CreateLocalTimestamp(DateTime localTime)
    {
        return new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
    }
}