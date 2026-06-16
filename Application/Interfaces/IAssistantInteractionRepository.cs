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
}