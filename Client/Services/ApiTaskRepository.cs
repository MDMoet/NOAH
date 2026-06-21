using Client.Models;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Tasks;

namespace Client.Services;

public sealed class ApiTaskRepository(NoahApiClient apiClient) : ITaskRepository
{
    public async Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TaskItemDto> tasks = await apiClient.GetAsync<IReadOnlyList<TaskItemDto>>("tasks", cancellationToken);
        return tasks
            .OrderBy(task => task.DueAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(task => task.CreatedAtUtc)
            .Select(Map)
            .ToList();
    }

    public async Task<IReadOnlyList<TaskItem>> GetTodayAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TaskItem> tasks = await GetAllAsync(cancellationToken);
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);

        return tasks
            .Where(task => task.IsRelevantForDate(today))
            .OrderBy(task => task.DueAtUtc ?? DateTimeOffset.MaxValue)
            .ToList();
    }

    public async Task<TaskItem> SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        if (task.Id == Guid.Empty)
        {
            CreateTaskItemRequest request = new(
                task.Title.Trim(),
                string.IsNullOrWhiteSpace(task.Description) ? null : task.Description.Trim(),
                task.Priority,
                task.DueAtUtc,
                task.PlannedFor);

            TaskItemDto created = await apiClient.PostAsync<CreateTaskItemRequest, TaskItemDto>("tasks", request, cancellationToken);
            return Map(created);
        }

        UpdateTaskItemRequest updateRequest = new(
            task.Title.Trim(),
            string.IsNullOrWhiteSpace(task.Description) ? null : task.Description.Trim(),
            task.Status,
            task.Priority,
            task.DueAtUtc,
            task.PlannedFor);

        TaskItemDto updated = await apiClient.PutAsync<UpdateTaskItemRequest, TaskItemDto>($"tasks/{task.Id:D}", updateRequest, cancellationToken);
        return Map(updated);
    }

    public async Task ToggleCompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TaskItemDto> tasks = await apiClient.GetAsync<IReadOnlyList<TaskItemDto>>("tasks", cancellationToken);
        TaskItemDto task = tasks.First(taskItem => taskItem.Id == id);
        TaskItemStatusDto nextStatus = task.Status == TaskItemStatusDto.Completed
            ? TaskItemStatusDto.Open
            : TaskItemStatusDto.Completed;

        UpdateTaskItemRequest request = new(
            task.Title,
            task.Description,
            nextStatus,
            task.Priority,
            task.DueAtUtc,
            task.PlannedFor);

        await apiClient.PutAsync<UpdateTaskItemRequest, TaskItemDto>($"tasks/{id:D}", request, cancellationToken);
    }

    private static TaskItem Map(TaskItemDto taskDto)
    {
        return new TaskItem
        {
            Id = taskDto.Id,
            Title = taskDto.Title,
            Description = taskDto.Description,
            Status = taskDto.Status,
            Priority = taskDto.Priority,
            DueAtUtc = taskDto.DueAtUtc,
            PlannedFor = taskDto.PlannedFor,
            CreatedAtUtc = taskDto.CreatedAtUtc,
            UpdatedAtUtc = taskDto.UpdatedAtUtc
        };
    }
}
