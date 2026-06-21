using Client.Models;

namespace Client.Services;

public sealed class StubAiChatService : IAiChatService
{
    public Task<string> SendAsync(IEnumerable<ChatMessage> history, string userMessage)
    {
        return Task.FromResult("I'm NOAH, your assistant. I'll be able to help you once connected to the assistant backend.");
    }
}
