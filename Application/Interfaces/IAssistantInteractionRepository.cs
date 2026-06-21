using NOAH.Domain.Entities;

namespace Application.Interfaces;

/// <summary>
/// Provides persistence operations for assistant interactions.
/// </summary>
public interface IAssistantInteractionRepository
{
    /// <summary>
    /// Adds a new assistant interaction.
    /// </summary>
    /// <param name="assistantInteraction">The interaction to add.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task AddAsync(AssistantInteraction assistantInteraction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing assistant interaction.
    /// </summary>
    /// <param name="assistantInteraction">The interaction to update.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task UpdateAsync(AssistantInteraction assistantInteraction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent completed assistant interactions for prompt memory.
    /// </summary>
    /// <param name="take">The maximum number of completed interactions to return.</param>
    /// <param name="excludeInteractionId">An optional interaction id to exclude from the result.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The most recent completed assistant interactions.</returns>
    Task<IReadOnlyList<AssistantInteraction>> GetRecentCompletedForScopeAsync(
        Guid? chatId,
        int take,
        Guid? excludeInteractionId = null,
        CancellationToken cancellationToken = default);
}
