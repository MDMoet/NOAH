using Application.Models;
using Application.Services;
using NOAH.Contracts.Assistant;
using NOAH.Contracts.Enums;

namespace Client.UnitTests;

public sealed class AssistantPromptBuilderTests
{
    [Fact]
    public void BuildPrompt_InstructsModelToUseMarkdownAsRenderedChatFormatting()
    {
        AssistantPromptBuilder builder = new();
        AssistantCommandRequest request = new(
            "Tell me about my day",
            AssistantInputModeDto.Text,
            AssistantResponseModeDto.Text,
            null,
            DateTimeOffset.Parse("2026-06-30T10:00:00Z"),
            null);

        string prompt = builder.BuildPrompt(request, new AssistantPromptContext
        {
            CurrentDateTimeUtc = DateTimeOffset.Parse("2026-06-30T10:00:00Z")
        });

        Assert.Contains("Use Markdown naturally as chat formatting", prompt);
        Assert.Contains("do not wrap the whole answer in a fenced ```markdown block", prompt);
        Assert.Contains("explicitly asks for Markdown source", prompt);
        Assert.Contains("one short acknowledgement and stop", prompt);
        Assert.Contains("generic follow-up offers", prompt);
        Assert.Contains("Do not suggest creating notes, tasks, reminders", prompt);
    }
}
