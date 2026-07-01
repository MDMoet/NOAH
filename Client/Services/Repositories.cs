using Client.Models;
using NOAH.Contracts.Reminders;

namespace Client.Services;

public interface INoteRepository
{
    Task<IReadOnlyList<Note>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Note>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default);
    Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Note>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task<Note> SaveAsync(Note note, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ITaskRepository
{
    Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskItem>> GetTodayAsync(CancellationToken cancellationToken = default);
    Task<TaskItem> SaveAsync(TaskItem task, CancellationToken cancellationToken = default);
    Task ToggleCompleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IReminderRepository
{
    Task<IReadOnlyList<ReminderDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ReminderDto> SaveAsync(ReminderDto reminder, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IMileageRepository
{
    Task<double> GetOdometerAsync(CancellationToken cancellationToken = default);
    Task<double> GetLastTripKmAsync(CancellationToken cancellationToken = default);
    Task<double> GetThisMonthKmAsync(CancellationToken cancellationToken = default);
    Task<DateTime> GetLastRecordedAtAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MileageEntry>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default);
    Task<MileageEntry> SaveAsync(MileageEntry entry, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IAiChatService
{
    Task<string> SendAsync(IEnumerable<ChatMessage> history, string userMessage);
}
