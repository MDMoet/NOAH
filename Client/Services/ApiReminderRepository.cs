using NOAH.Contracts.Reminders;

namespace Client.Services;

public sealed class ApiReminderRepository(NoahApiClient apiClient) : IReminderRepository
{
    public async Task<IReadOnlyList<ReminderDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReminderDto> reminders = await apiClient.GetAsync<IReadOnlyList<ReminderDto>>("reminders", cancellationToken);
        return reminders
            .OrderBy(reminder => reminder.TriggerAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(reminder => reminder.CreatedAtUtc)
            .ToList();
    }

    public async Task<ReminderDto> SaveAsync(ReminderDto reminder, CancellationToken cancellationToken = default)
    {
        if (reminder.Id == Guid.Empty)
        {
            CreateReminderRequest request = new(
                reminder.Title,
                reminder.Message,
                reminder.TriggerType,
                reminder.ShouldNotify,
                reminder.TriggerAtUtc,
                reminder.TriggerLocation,
                reminder.TriggerRadiusMeters,
                reminder.TaskItemId,
                reminder.NoteId,
                reminder.SavedLocationId);

            return await apiClient.PostAsync<CreateReminderRequest, ReminderDto>("reminders", request, cancellationToken);
        }

        UpdateReminderRequest updateRequest = new(
            reminder.Title,
            reminder.Message,
            reminder.TriggerType,
            reminder.Status,
            reminder.ShouldNotify,
            reminder.TriggerAtUtc,
            reminder.TriggerLocation,
            reminder.TriggerRadiusMeters,
            reminder.TaskItemId,
            reminder.NoteId,
            reminder.SavedLocationId);

        return await apiClient.PutAsync<UpdateReminderRequest, ReminderDto>($"reminders/{reminder.Id:D}", updateRequest, cancellationToken);
    }
}
