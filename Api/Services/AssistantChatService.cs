using Application.Interfaces;
using Application.Models;
using Microsoft.EntityFrameworkCore;
using NOAH.Contracts.Assistant;
using NOAH.Contracts.Enums;
using NOAH.Domain.Entities;
using NOAH.Domain.Enums;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

/// <summary>
/// Persists assistant chat threads and their scoped message history.
/// </summary>
public sealed class AssistantChatService(
    NoahDbContext noahDbContext,
    TimeProvider timeProvider)
    : IAssistantChatService
{
    private const string DefaultChatTitle = "New chat";
    private const int MaximumChatTitleLength = 80;
    private const int MaximumPreviewLength = 300;
    private const int MaximumMessagePageSize = 200;

    /// <summary>
    /// Gets all assistant chats ordered by most recent activity.
    /// </summary>
    public async Task<IReadOnlyList<AssistantChatDto>> GetChatsAsync(CancellationToken cancellationToken = default)
    {
        List<AssistantChatDto> chats = await noahDbContext.AssistantChats
            .AsNoTracking()
            .OrderByDescending(assistantChat => assistantChat.LastMessageAtUtc ?? assistantChat.CreatedAtUtc)
            .Select(assistantChat => new AssistantChatDto(
                assistantChat.Id,
                assistantChat.Title,
                assistantChat.Description,
                assistantChat.IsArchived,
                noahDbContext.AssistantInteractions.Count(assistantInteraction => assistantInteraction.ChatId == assistantChat.Id),
                assistantChat.LastMessagePreview,
                assistantChat.LastMessageAtUtc,
                assistantChat.CreatedAtUtc,
                assistantChat.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return chats;
    }

    /// <summary>
    /// Gets one assistant chat by id.
    /// </summary>
    public async Task<AssistantChatDto?> GetChatByIdAsync(Guid chatId, CancellationToken cancellationToken = default)
    {
        AssistantChatDto? chat = await noahDbContext.AssistantChats
            .AsNoTracking()
            .Where(assistantChat => assistantChat.Id == chatId)
            .Select(assistantChat => new AssistantChatDto(
                assistantChat.Id,
                assistantChat.Title,
                assistantChat.Description,
                assistantChat.IsArchived,
                noahDbContext.AssistantInteractions.Count(assistantInteraction => assistantInteraction.ChatId == assistantChat.Id),
                assistantChat.LastMessagePreview,
                assistantChat.LastMessageAtUtc,
                assistantChat.CreatedAtUtc,
                assistantChat.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return chat;
    }

    /// <summary>
    /// Returns true when a chat exists.
    /// </summary>
    public async Task<bool> ExistsAsync(Guid chatId, CancellationToken cancellationToken = default)
    {
        return await noahDbContext.AssistantChats
            .AsNoTracking()
            .AnyAsync(assistantChat => assistantChat.Id == chatId, cancellationToken);
    }

    /// <summary>
    /// Creates a new assistant chat thread.
    /// </summary>
    public async Task<AssistantChatDto> CreateChatAsync(
        CreateAssistantChatRequest request,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset currentDateTimeUtc = timeProvider.GetUtcNow();
        AssistantChat assistantChat = new()
        {
            Id = Guid.NewGuid(),
            Title = NormalizeChatTitle(request.Title),
            Description = NormalizeOptionalText(request.Description),
            IsArchived = false,
            LastMessagePreview = null,
            LastMessageAtUtc = null,
            CreatedAtUtc = currentDateTimeUtc,
            UpdatedAtUtc = null
        };

        noahDbContext.AssistantChats.Add(assistantChat);
        await noahDbContext.SaveChangesAsync(cancellationToken);

        return await GetChatByIdAsync(assistantChat.Id, cancellationToken)
               ?? new AssistantChatDto(
                   assistantChat.Id,
                   assistantChat.Title,
                   assistantChat.Description,
                   assistantChat.IsArchived,
                   0,
                   assistantChat.LastMessagePreview,
                   assistantChat.LastMessageAtUtc,
                   assistantChat.CreatedAtUtc,
                   assistantChat.UpdatedAtUtc);
    }

    /// <summary>
    /// Updates the editable metadata of an assistant chat.
    /// </summary>
    public async Task<AssistantChatDto?> UpdateChatAsync(
        Guid chatId,
        UpdateAssistantChatRequest request,
        CancellationToken cancellationToken = default)
    {
        AssistantChat? assistantChat = await noahDbContext.AssistantChats
            .FirstOrDefaultAsync(assistantChat => assistantChat.Id == chatId, cancellationToken);

        if (assistantChat == null)
        {
            return null;
        }

        if (request.Title != null)
        {
            assistantChat.Title = NormalizeChatTitle(request.Title);
        }

        if (request.Description != null)
        {
            assistantChat.Description = NormalizeOptionalText(request.Description);
        }

        if (request.IsArchived.HasValue)
        {
            assistantChat.IsArchived = request.IsArchived.Value;
        }

        assistantChat.UpdatedAtUtc = timeProvider.GetUtcNow();

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return await GetChatByIdAsync(chatId, cancellationToken);
    }

    /// <summary>
    /// Deletes a chat and the assistant interactions scoped to it.
    /// </summary>
    public async Task<bool> DeleteChatAsync(Guid chatId, CancellationToken cancellationToken = default)
    {
        AssistantChat? assistantChat = await noahDbContext.AssistantChats
            .FirstOrDefaultAsync(assistantChat => assistantChat.Id == chatId, cancellationToken);

        if (assistantChat == null)
        {
            return false;
        }

        List<AssistantMemoryItem> memoryItems = await noahDbContext.AssistantMemoryItems
            .Where(memoryItem => memoryItem.SourceChatId == chatId)
            .ToListAsync(cancellationToken);
        List<AssistantInteraction> interactions = await noahDbContext.AssistantInteractions
            .Where(assistantInteraction => assistantInteraction.ChatId == chatId)
            .ToListAsync(cancellationToken);

        if (memoryItems.Count > 0)
        {
            noahDbContext.AssistantMemoryItems.RemoveRange(memoryItems);
        }

        if (interactions.Count > 0)
        {
            noahDbContext.AssistantInteractions.RemoveRange(interactions);
        }

        noahDbContext.AssistantChats.Remove(assistantChat);
        await noahDbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Gets recent chat messages ordered oldest-to-newest within the requested slice.
    /// </summary>
    public async Task<IReadOnlyList<AssistantInteractionDto>> GetMessagesAsync(
        Guid chatId,
        int take,
        CancellationToken cancellationToken = default)
    {
        int normalizedTake = Math.Clamp(take, 1, MaximumMessagePageSize);
        List<AssistantInteractionDto> messages = await noahDbContext.AssistantInteractions
            .AsNoTracking()
            .Where(assistantInteraction => assistantInteraction.ChatId == chatId)
            .OrderByDescending(assistantInteraction => assistantInteraction.RequestedAtUtc)
            .Take(normalizedTake)
            .OrderBy(assistantInteraction => assistantInteraction.RequestedAtUtc)
            .Select(assistantInteraction => new AssistantInteractionDto(
                assistantInteraction.Id,
                assistantInteraction.ChatId,
                assistantInteraction.UserInput,
                (AssistantInputModeDto)assistantInteraction.InputMode,
                (AssistantActionTypeDto)assistantInteraction.ActionType,
                assistantInteraction.AssistantResponse,
                (AssistantResponseModeDto)assistantInteraction.ResponseMode,
                (AssistantInteractionStatusDto)assistantInteraction.Status,
                assistantInteraction.RelatedEntityId,
                assistantInteraction.RelatedEntityType,
                assistantInteraction.ErrorMessage,
                assistantInteraction.RequestedAtUtc,
                assistantInteraction.CompletedAtUtc))
            .ToListAsync(cancellationToken);

        return messages;
    }

    /// <summary>
    /// Gets lightweight prompt metadata for one chat thread.
    /// </summary>
    public async Task<AssistantChatPromptInfo?> GetPromptInfoAsync(
        Guid chatId,
        CancellationToken cancellationToken = default)
    {
        AssistantChatPromptInfo? promptInfo = await noahDbContext.AssistantChats
            .AsNoTracking()
            .Where(assistantChat => assistantChat.Id == chatId)
            .Select(assistantChat => new AssistantChatPromptInfo(
                assistantChat.Id,
                assistantChat.Title,
                assistantChat.Description))
            .FirstOrDefaultAsync(cancellationToken);

        return promptInfo;
    }

    /// <summary>
    /// Records chat activity metadata when a new user message is received.
    /// </summary>
    public async Task RecordInteractionAsync(
        Guid chatId,
        string userInput,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        AssistantChat? assistantChat = await noahDbContext.AssistantChats
            .FirstOrDefaultAsync(assistantChat => assistantChat.Id == chatId, cancellationToken);

        if (assistantChat == null)
        {
            return;
        }

        if (IsPlaceholderTitle(assistantChat.Title))
        {
            assistantChat.Title = GenerateChatTitle(userInput);
        }

        assistantChat.LastMessageAtUtc = requestedAtUtc;
        assistantChat.LastMessagePreview = CreatePreview(userInput);
        assistantChat.UpdatedAtUtc = timeProvider.GetUtcNow();

        await noahDbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsPlaceholderTitle(string? title)
    {
        return string.IsNullOrWhiteSpace(title) ||
               string.Equals(title.Trim(), DefaultChatTitle, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeChatTitle(string? title)
    {
        string normalizedTitle = NormalizeWhitespace(title);

        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return DefaultChatTitle;
        }

        return normalizedTitle.Length <= MaximumChatTitleLength
            ? normalizedTitle
            : normalizedTitle[..MaximumChatTitleLength].TrimEnd();
    }

    private static string GenerateChatTitle(string value)
    {
        string normalizedValue = NormalizeWhitespace(value);

        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return DefaultChatTitle;
        }

        int sentenceEndIndex = normalizedValue.IndexOfAny(['.', '!', '?']);
        string generatedTitle = sentenceEndIndex > 0
            ? normalizedValue[..sentenceEndIndex]
            : normalizedValue;

        return generatedTitle.Length <= MaximumChatTitleLength
            ? generatedTitle
            : generatedTitle[..MaximumChatTitleLength].TrimEnd();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        string normalizedValue = NormalizeWhitespace(value);
        return string.IsNullOrWhiteSpace(normalizedValue) ? null : normalizedValue;
    }

    private static string CreatePreview(string value)
    {
        string normalizedValue = NormalizeWhitespace(value);

        if (normalizedValue.Length <= MaximumPreviewLength)
        {
            return normalizedValue;
        }

        const string ellipsis = "...";
        return normalizedValue[..(MaximumPreviewLength - ellipsis.Length)].TrimEnd() + ellipsis;
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            value.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }
}
