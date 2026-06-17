using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using NOAH.Contracts.Assistant;
using NOAH.Contracts.Enums;
using NOAH.Domain.Entities;
using NOAH.Domain.Enums;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

/// <summary>
/// Persists assistant-level user settings and memory preferences.
/// </summary>
public sealed class AssistantSettingsService(
    NoahDbContext noahDbContext,
    TimeProvider timeProvider)
    : IAssistantSettingsService
{
    /// <summary>
    /// Gets the stored assistant settings, creating a default row when none exists yet.
    /// </summary>
    public async Task<AssistantSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        AssistantSettings assistantSettings = await GetOrCreateSettingsEntityAsync(cancellationToken);
        return MapToDto(assistantSettings);
    }

    /// <summary>
    /// Updates the assistant settings row.
    /// </summary>
    public async Task<AssistantSettingsDto> UpdateSettingsAsync(
        UpdateAssistantSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        AssistantSettings assistantSettings = await GetOrCreateSettingsEntityAsync(cancellationToken);

        assistantSettings.PreferredResponseMode = (AssistantResponseMode)request.PreferredResponseMode;
        assistantSettings.SpeechCulture = request.SpeechCulture.Trim();
        assistantSettings.EnableChatMemory = request.EnableChatMemory;
        assistantSettings.EnableLongTermMemory = request.EnableLongTermMemory;
        assistantSettings.EnableMemoryCapture = request.EnableMemoryCapture;
        assistantSettings.ConversationMemoryMessageCount = Math.Clamp(request.ConversationMemoryMessageCount, 0, 20);
        assistantSettings.LongTermMemoryItemCount = Math.Clamp(request.LongTermMemoryItemCount, 0, 20);
        assistantSettings.UpdatedAtUtc = timeProvider.GetUtcNow();

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(assistantSettings);
    }

    private async Task<AssistantSettings> GetOrCreateSettingsEntityAsync(CancellationToken cancellationToken)
    {
        AssistantSettings? assistantSettings = await noahDbContext.AssistantSettings
            .OrderBy(assistantSettings => assistantSettings.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (assistantSettings != null)
        {
            return assistantSettings;
        }

        assistantSettings = new AssistantSettings
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        noahDbContext.AssistantSettings.Add(assistantSettings);
        await noahDbContext.SaveChangesAsync(cancellationToken);

        return assistantSettings;
    }

    private static AssistantSettingsDto MapToDto(AssistantSettings assistantSettings)
    {
        return new AssistantSettingsDto(
            assistantSettings.Id,
            (AssistantResponseModeDto)assistantSettings.PreferredResponseMode,
            assistantSettings.SpeechCulture,
            assistantSettings.EnableChatMemory,
            assistantSettings.EnableLongTermMemory,
            assistantSettings.EnableMemoryCapture,
            assistantSettings.ConversationMemoryMessageCount,
            assistantSettings.LongTermMemoryItemCount);
    }
}
