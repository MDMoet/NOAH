using NOAH.Contracts.Reminders;

namespace Api.Interfaces;

public interface IRemindersService
{
    /// <summary>
    /// Gets all reminders that are currently available.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A read-only list of reminders.</returns>
    Task<IReadOnlyList<ReminderDto>> GetRemindersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a reminder by its unique identifier.
    /// </summary>
    /// <param name="reminderId">The unique identifier of the reminder.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The reminder when found; otherwise, null.</returns>
    Task<ReminderDto?> GetReminderByIdAsync(Guid reminderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new reminder using the provided reminder details.
    /// </summary>
    /// <param name="request">The reminder details used to create the reminder.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The created reminder.</returns>
    Task<ReminderDto> CreateReminderAsync(CreateReminderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully updates an existing reminder by replacing its editable fields.
    /// </summary>
    /// <param name="reminderId">The unique identifier of the reminder to update.</param>
    /// <param name="request">The new reminder details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated reminder when found; otherwise, null.</returns>
    Task<ReminderDto?> UpdateReminderAsync(
        Guid reminderId,
        UpdateReminderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an existing reminder by its unique identifier.
    /// </summary>
    /// <param name="reminderId">The unique identifier of the reminder to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True when the reminder was deleted; otherwise, false.</returns>
    Task<bool> DeleteReminderAsync(Guid reminderId, CancellationToken cancellationToken = default);
}
