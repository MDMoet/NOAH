using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using NOAH.Domain.Entities;
using NOAH.Domain.Enums;
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

    /// <summary>
    /// Gets recent completed assistant interactions for prompt memory.
    /// </summary>
    /// <param name="take">The maximum number of interactions to return.</param>
    /// <param name="excludeInteractionId">An optional interaction id to exclude from the result.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The most recent completed assistant interactions.</returns>
    public async Task<IReadOnlyList<AssistantInteraction>> GetRecentCompletedForScopeAsync(
        Guid? chatId,
        int take,
        Guid? excludeInteractionId = null,
        CancellationToken cancellationToken = default)
    {
        int normalizedTake = Math.Clamp(take, 1, 20);
        IQueryable<AssistantInteraction> query = noahDbContext.AssistantInteractions
            .AsNoTracking()
            .Where(assistantInteraction =>
                assistantInteraction.Status == AssistantInteractionStatus.Completed &&
                assistantInteraction.AssistantResponse != null);

        query = chatId.HasValue
            ? query.Where(assistantInteraction => assistantInteraction.ChatId == chatId.Value)
            : query.Where(assistantInteraction => assistantInteraction.ChatId == null);

        if (excludeInteractionId.HasValue)
        {
            query = query.Where(assistantInteraction => assistantInteraction.Id != excludeInteractionId.Value);
        }

        return await query
            .OrderByDescending(assistantInteraction => assistantInteraction.RequestedAtUtc)
            .Take(normalizedTake)
            .ToListAsync(cancellationToken);
    }
}
