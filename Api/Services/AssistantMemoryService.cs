using System.Text;
using Application.Interfaces;
using Application.Models;
using Microsoft.EntityFrameworkCore;
using NOAH.Contracts.Assistant;
using NOAH.Domain.Entities;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

/// <summary>
/// Persists and retrieves long-term assistant memory items.
/// </summary>
public sealed class AssistantMemoryService(
    NoahDbContext noahDbContext,
    TimeProvider timeProvider)
    : IAssistantMemoryService
{
    private const int MaximumRelevantMemoryTake = 20;
    private const int CandidateMemoryLimit = 100;

    /// <summary>
    /// Gets all persisted assistant memory items.
    /// </summary>
    public async Task<IReadOnlyList<AssistantMemoryItemDto>> GetMemoryItemsAsync(CancellationToken cancellationToken = default)
    {
        List<AssistantMemoryItemDto> memoryItems = await noahDbContext.AssistantMemoryItems
            .AsNoTracking()
            .OrderByDescending(memoryItem => memoryItem.IsPinned)
            .ThenByDescending(memoryItem => memoryItem.UpdatedAtUtc ?? memoryItem.CreatedAtUtc)
            .Select(memoryItem => new AssistantMemoryItemDto(
                memoryItem.Id,
                memoryItem.Title,
                memoryItem.Content,
                memoryItem.Tags,
                memoryItem.IsPinned,
                memoryItem.SourceInteractionId,
                memoryItem.SourceChatId,
                memoryItem.CreatedAtUtc,
                memoryItem.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return memoryItems;
    }

    /// <summary>
    /// Gets one persisted assistant memory item by id.
    /// </summary>
    public async Task<AssistantMemoryItemDto?> GetMemoryItemByIdAsync(
        Guid memoryItemId,
        CancellationToken cancellationToken = default)
    {
        AssistantMemoryItemDto? memoryItem = await noahDbContext.AssistantMemoryItems
            .AsNoTracking()
            .Where(memoryItem => memoryItem.Id == memoryItemId)
            .Select(memoryItem => new AssistantMemoryItemDto(
                memoryItem.Id,
                memoryItem.Title,
                memoryItem.Content,
                memoryItem.Tags,
                memoryItem.IsPinned,
                memoryItem.SourceInteractionId,
                memoryItem.SourceChatId,
                memoryItem.CreatedAtUtc,
                memoryItem.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return memoryItem;
    }

    /// <summary>
    /// Creates a new assistant memory item.
    /// </summary>
    public async Task<AssistantMemoryItemDto> CreateMemoryItemAsync(
        CreateAssistantMemoryItemRequest request,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset currentDateTimeUtc = timeProvider.GetUtcNow();
        AssistantMemoryItem memoryItem = new()
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            Tags = NormalizeOptionalText(request.Tags),
            IsPinned = request.IsPinned,
            SourceInteractionId = await NormalizeSourceInteractionIdAsync(request.SourceInteractionId, cancellationToken),
            SourceChatId = await NormalizeSourceChatIdAsync(request.SourceChatId, cancellationToken),
            CreatedAtUtc = currentDateTimeUtc,
            UpdatedAtUtc = null
        };

        noahDbContext.AssistantMemoryItems.Add(memoryItem);
        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(memoryItem);
    }

    /// <summary>
    /// Updates an existing assistant memory item.
    /// </summary>
    public async Task<AssistantMemoryItemDto?> UpdateMemoryItemAsync(
        Guid memoryItemId,
        UpdateAssistantMemoryItemRequest request,
        CancellationToken cancellationToken = default)
    {
        AssistantMemoryItem? memoryItem = await noahDbContext.AssistantMemoryItems
            .FirstOrDefaultAsync(memoryItem => memoryItem.Id == memoryItemId, cancellationToken);

        if (memoryItem == null)
        {
            return null;
        }

        memoryItem.Title = request.Title.Trim();
        memoryItem.Content = request.Content.Trim();
        memoryItem.Tags = NormalizeOptionalText(request.Tags);
        memoryItem.IsPinned = request.IsPinned;
        memoryItem.UpdatedAtUtc = timeProvider.GetUtcNow();

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(memoryItem);
    }

    /// <summary>
    /// Deletes a persisted assistant memory item.
    /// </summary>
    public async Task<bool> DeleteMemoryItemAsync(Guid memoryItemId, CancellationToken cancellationToken = default)
    {
        AssistantMemoryItem? memoryItem = await noahDbContext.AssistantMemoryItems
            .FirstOrDefaultAsync(memoryItem => memoryItem.Id == memoryItemId, cancellationToken);

        if (memoryItem == null)
        {
            return false;
        }

        noahDbContext.AssistantMemoryItems.Remove(memoryItem);
        await noahDbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Gets memory items that are most relevant to the current assistant input.
    /// </summary>
    public async Task<IReadOnlyList<AssistantLongTermMemoryEntry>> GetRelevantMemoryAsync(
        string input,
        int take,
        CancellationToken cancellationToken = default)
    {
        int normalizedTake = Math.Clamp(take, 0, MaximumRelevantMemoryTake);

        if (normalizedTake == 0)
        {
            return Array.Empty<AssistantLongTermMemoryEntry>();
        }

        List<AssistantMemoryItem> candidates = await noahDbContext.AssistantMemoryItems
            .AsNoTracking()
            .OrderByDescending(memoryItem => memoryItem.IsPinned)
            .ThenByDescending(memoryItem => memoryItem.UpdatedAtUtc ?? memoryItem.CreatedAtUtc)
            .Take(CandidateMemoryLimit)
            .ToListAsync(cancellationToken);

        string normalizedInput = NormalizeText(input);
        string[] tokens = ExtractTokens(normalizedInput);

        IReadOnlyList<AssistantLongTermMemoryEntry> relevantMemory = candidates
            .Select(memoryItem => new
            {
                MemoryItem = memoryItem,
                Score = ScoreMemory(memoryItem, normalizedInput, tokens)
            })
            .Where(result => result.Score > 0 || result.MemoryItem.IsPinned)
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.MemoryItem.IsPinned)
            .ThenByDescending(result => result.MemoryItem.UpdatedAtUtc ?? result.MemoryItem.CreatedAtUtc)
            .Take(normalizedTake)
            .Select(result => new AssistantLongTermMemoryEntry(
                result.MemoryItem.Id,
                result.MemoryItem.Title,
                result.MemoryItem.Content,
                result.MemoryItem.Tags,
                result.MemoryItem.IsPinned,
                result.MemoryItem.CreatedAtUtc))
            .ToList();

        if (relevantMemory.Count > 0)
        {
            return relevantMemory;
        }

        return candidates
            .Take(normalizedTake)
            .Select(memoryItem => new AssistantLongTermMemoryEntry(
                memoryItem.Id,
                memoryItem.Title,
                memoryItem.Content,
                memoryItem.Tags,
                memoryItem.IsPinned,
                memoryItem.CreatedAtUtc))
            .ToList();
    }

    private async Task<Guid?> NormalizeSourceInteractionIdAsync(
        Guid? sourceInteractionId,
        CancellationToken cancellationToken)
    {
        if (!sourceInteractionId.HasValue)
        {
            return null;
        }

        bool interactionExists = await noahDbContext.AssistantInteractions
            .AnyAsync(assistantInteraction => assistantInteraction.Id == sourceInteractionId.Value, cancellationToken);

        return interactionExists ? sourceInteractionId : null;
    }

    private async Task<Guid?> NormalizeSourceChatIdAsync(
        Guid? sourceChatId,
        CancellationToken cancellationToken)
    {
        if (!sourceChatId.HasValue)
        {
            return null;
        }

        bool chatExists = await noahDbContext.AssistantChats
            .AnyAsync(assistantChat => assistantChat.Id == sourceChatId.Value, cancellationToken);

        return chatExists ? sourceChatId : null;
    }

    private static int ScoreMemory(
        AssistantMemoryItem memoryItem,
        string normalizedInput,
        IReadOnlyList<string> tokens)
    {
        string normalizedTitle = NormalizeText(memoryItem.Title);
        string normalizedContent = NormalizeText(memoryItem.Content);
        string normalizedTags = NormalizeText(memoryItem.Tags);
        int score = memoryItem.IsPinned ? 100 : 0;

        if (!string.IsNullOrWhiteSpace(normalizedTitle) &&
            normalizedInput.Contains(normalizedTitle, StringComparison.Ordinal))
        {
            score += 50;
        }

        foreach (string token in tokens)
        {
            if (normalizedTitle.Contains(token, StringComparison.Ordinal))
            {
                score += 20;
            }

            if (!string.IsNullOrWhiteSpace(normalizedTags) &&
                normalizedTags.Contains(token, StringComparison.Ordinal))
            {
                score += 14;
            }

            if (normalizedContent.Contains(token, StringComparison.Ordinal))
            {
                score += 8;
            }
        }

        return score;
    }

    private static string[] ExtractTokens(string value)
    {
        return value
            .Split([' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        bool previousWasWhitespace = false;

        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasWhitespace = false;
                continue;
            }

            if (previousWasWhitespace)
            {
                continue;
            }

            builder.Append(' ');
            previousWasWhitespace = true;
        }

        return builder.ToString().Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static AssistantMemoryItemDto MapToDto(AssistantMemoryItem memoryItem)
    {
        return new AssistantMemoryItemDto(
            memoryItem.Id,
            memoryItem.Title,
            memoryItem.Content,
            memoryItem.Tags,
            memoryItem.IsPinned,
            memoryItem.SourceInteractionId,
            memoryItem.SourceChatId,
            memoryItem.CreatedAtUtc,
            memoryItem.UpdatedAtUtc);
    }
}
