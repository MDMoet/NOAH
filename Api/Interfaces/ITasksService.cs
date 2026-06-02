using NOAH.Contracts.Tasks;

namespace Api.Interfaces;

public interface ITasksService
{
    /// <summary>
    /// Gets all task items that are currently available.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A read-only list of task items.</returns>
    Task<IReadOnlyList<TaskItemDto>> GetTaskItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a task item by its unique identifier.
    /// </summary>
    /// <param name="taskItemId">The unique identifier of the task item.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The task item when found; otherwise, null.</returns>
    Task<TaskItemDto?> GetTaskItemByIdAsync(Guid taskItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new task item using the provided task details.
    /// </summary>
    /// <param name="request">The task details used to create the task item.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The created task item.</returns>
    Task<TaskItemDto> CreateTaskItemAsync(CreateTaskItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully updates an existing task item by replacing its editable fields.
    /// </summary>
    /// <param name="taskItemId">The unique identifier of the task item to update.</param>
    /// <param name="request">The new task item details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated task item when found; otherwise, null.</returns>
    Task<TaskItemDto?> UpdateTaskItemAsync(
        Guid taskItemId,
        UpdateTaskItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an existing task item by its unique identifier.
    /// </summary>
    /// <param name="taskItemId">The unique identifier of the task item to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True when the task item was deleted; otherwise, false.</returns>
    Task<bool> DeleteTaskItemAsync(Guid taskItemId, CancellationToken cancellationToken = default);
}
