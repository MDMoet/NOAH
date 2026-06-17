using Application.Models;
using NOAH.Contracts.Assistant;

namespace Application.Interfaces;

/// <summary>
/// Manages persisted long-term assistant memory entries.
/// </summary>
public interface IAssistantMemoryService
{
    /// <summary>
    /// Gets all assistant memory items ordered by relevance for management screens.
    /// </summary>
    Task<IReadOnlyList<AssistantMemoryItemDto>> GetMemoryItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one assistant memory item by id.
    /// </summary>
    Task<AssistantMemoryItemDto?> GetMemoryItemByIdAsync(Guid memoryItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new long-term memory item.
    /// </summary>
    Task<AssistantMemoryItemDto> CreateMemoryItemAsync(
        CreateAssistantMemoryItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing long-term memory item.
    /// </summary>
    Task<AssistantMemoryItemDto?> UpdateMemoryItemAsync(
        Guid memoryItemId,
        UpdateAssistantMemoryItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a persisted long-term memory item.
    /// </summary>
    Task<bool> DeleteMemoryItemAsync(Guid memoryItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets memory items that are relevant to the current assistant request.
    /// </summary>
    Task<IReadOnlyList<AssistantLongTermMemoryEntry>> GetRelevantMemoryAsync(
        string input,
        int take,
        CancellationToken cancellationToken = default);
}
