using Application.Interfaces;
using NOAH.Domain.Entities;
using NOAH.Infrastructure.Persistence;

namespace Infrastructure.Repository;

/// <summary>
/// Persists assistant interactions using the NOAH database context.
/// </summary>
public sealed class AssistantInteractionRepository(NoahDbContext noahDbContext) : IAssistantInteractionRepository
{
    /// <summary>
    /// Adds a new assistant interaction and saves changes.
    /// </summary>
    /// <param name="interaction">The interaction to add.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task AddAsync(AssistantInteraction interaction, CancellationToken cancellationToken = default)
    {
        await noahDbContext.AssistantInteractions.AddAsync(interaction, cancellationToken);
        await noahDbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Updates an existing assistant interaction and saves changes.
    /// </summary>
    /// <param name="interaction">The interaction to update.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task UpdateAsync(AssistantInteraction interaction, CancellationToken cancellationToken = default)
    {
        noahDbContext.AssistantInteractions.Update(interaction);
        await noahDbContext.SaveChangesAsync(cancellationToken);
    }
}
