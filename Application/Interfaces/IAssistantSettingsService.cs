using NOAH.Contracts.Assistant;

namespace Application.Interfaces;

/// <summary>
/// Provides persisted assistant-level user preferences and memory settings.
/// </summary>
public interface IAssistantSettingsService
{
    /// <summary>
    /// Gets the stored assistant settings, creating defaults when none exist yet.
    /// </summary>
    Task<AssistantSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the persisted assistant settings.
    /// </summary>
    Task<AssistantSettingsDto> UpdateSettingsAsync(
        UpdateAssistantSettingsRequest request,
        CancellationToken cancellationToken = default);
}
