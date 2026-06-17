using Application.Models;
using NOAH.Contracts.Assistant;

namespace Application.Interfaces;

/// <summary>
/// Manages assistant chat threads and their persisted message history.
/// </summary>
public interface IAssistantChatService
{
    /// <summary>
    /// Gets all assistant chats ordered by latest activity.
    /// </summary>
    Task<IReadOnlyList<AssistantChatDto>> GetChatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one assistant chat by id.
    /// </summary>
    Task<AssistantChatDto?> GetChatByIdAsync(Guid chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when a chat exists.
    /// </summary>
    Task<bool> ExistsAsync(Guid chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new assistant chat thread.
    /// </summary>
    Task<AssistantChatDto> CreateChatAsync(CreateAssistantChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the editable metadata of an assistant chat thread.
    /// </summary>
    Task<AssistantChatDto?> UpdateChatAsync(
        Guid chatId,
        UpdateAssistantChatRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a chat and its scoped assistant messages.
    /// </summary>
    Task<bool> DeleteChatAsync(Guid chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent messages that belong to a single chat thread.
    /// </summary>
    Task<IReadOnlyList<AssistantInteractionDto>> GetMessagesAsync(
        Guid chatId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets lightweight chat metadata for prompt construction.
    /// </summary>
    Task<AssistantChatPromptInfo?> GetPromptInfoAsync(Guid chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates activity markers for a chat when a new message is received.
    /// </summary>
    Task RecordInteractionAsync(
        Guid chatId,
        string userInput,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);
}
