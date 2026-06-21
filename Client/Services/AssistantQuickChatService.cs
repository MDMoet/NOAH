using Client.Models;
using NOAH.Contracts.Enums;

namespace Client.Services;

public sealed class AssistantQuickChatService(
    AssistantApiService assistantApiService,
    AssistantApiSettingsService settingsService) : IAiChatService
{
    public async Task<string> SendAsync(IEnumerable<ChatMessage> history, string userMessage)
    {
        Guid chatId = await EnsureChatAsync();
        AssistantCommandRequest request = new(
            userMessage,
            AssistantInputModeDto.Text.ToString(),
            AssistantResponseModeDto.Text.ToString(),
            null,
            DateTimeOffset.UtcNow,
            chatId);

        AssistantCommandResponse response = await assistantApiService.SendChatMessageAsync(chatId, request);
        return response.ResponseText;
    }

    private async Task<Guid> EnsureChatAsync()
    {
        AssistantClientSettings settings = settingsService.Load();
        if (settings.LastSelectedChatId.HasValue)
        {
            return settings.LastSelectedChatId.Value;
        }

        AssistantChatDto chat = await assistantApiService.CreateChatAsync(new CreateAssistantChatRequest("Quick chat", "Created from the home assistant."));
        settingsService.SaveSelectedChatId(chat.Id);
        return chat.Id;
    }
}
