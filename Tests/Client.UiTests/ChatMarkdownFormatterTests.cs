using Client.Models;

namespace Client.UiTests;

public sealed class ChatMarkdownFormatterTests
{
    [Fact]
    public void NormalizeMarkdownForRendering_UnwrapsWholeMarkdownFence()
    {
        string markdown = "```markdown\n# Heading 1\n\n[OpenAI](https://openai.com)\n```";

        string normalized = ChatMarkdownFormatter.NormalizeMarkdownForRendering(markdown);

        Assert.Equal("# Heading 1\n\n[OpenAI](https://openai.com)", normalized);
    }

    [Fact]
    public void NormalizeMarkdownForRendering_KeepsRealCodeFence()
    {
        string code = "```csharp\nConsole.WriteLine(\"NOAH\");\n```";

        string normalized = ChatMarkdownFormatter.NormalizeMarkdownForRendering(code);

        Assert.Equal(code, normalized);
    }

    [Theory]
    [InlineData("https://example.com/docs", "https://example.com/docs")]
    [InlineData("www.example.com/docs", "https://www.example.com/docs")]
    [InlineData("example.com/docs", "https://example.com/docs")]
    [InlineData("example.dev/docs", "https://example.dev/docs")]
    [InlineData("/local-page", null)]
    [InlineData("llama.cpp", null)]
    [InlineData("System.Text.Json", null)]
    [InlineData("Program.cs", null)]
    public void NormalizeLinkUrl_ReturnsLaunchableAbsoluteUrls(string input, string? expected)
    {
        Assert.Equal(expected, ChatMarkdownFormatter.NormalizeLinkUrl(input));
    }

    [Theory]
    [InlineData("http://llama.cpp", null)]
    [InlineData("http://System.Text.Json", null)]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("example.com/docs", "https://example.com/docs")]
    public void NormalizeLinkUrl_CanRejectAutoLinkedFileExtensionHosts(string input, string? expected)
    {
        Assert.Equal(expected, ChatMarkdownFormatter.NormalizeLinkUrl(input, rejectFileExtensionHost: true));
    }
    [Fact]
    public void ShouldRenderAsPlainSelectableEditor_UsesSingleEditorForLongPlainResponses()
    {
        string plainResponse = "This is a normal assistant response that spans enough text to wrap across multiple lines in the chat surface, but it does not contain markdown features or links that need custom rendering.";

        Assert.True(ChatMarkdownFormatter.ShouldRenderAsPlainSelectableEditor(plainResponse));
    }

    [Fact]
    public void ShouldRenderAsPlainSelectableEditor_KeepsMarkdownResponsesStyled()
    {
        Assert.False(ChatMarkdownFormatter.ShouldRenderAsPlainSelectableEditor("# Heading\n\n- One\n- Two"));
        Assert.False(ChatMarkdownFormatter.ShouldRenderAsPlainSelectableEditor("Open [NOAH](https://example.com)."));
    }
}
