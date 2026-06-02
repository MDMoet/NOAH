using Api.Interfaces;
using Microsoft.EntityFrameworkCore;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Tasks;
using NOAH.Domain.Entities;
using NOAH.Domain.Enums;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

public sealed class TasksService(NoahDbContext noahDbContext) : ITasksService
{
    public async Task<IReadOnlyList<TaskItemDto>> GetTaskItemsAsync(CancellationToken cancellationToken = default)
    {
        List<TaskItemDto> taskItems = await noahDbContext.TaskItems
            .AsNoTracking()
            .OrderBy(taskItem => taskItem.Status)
            .ThenByDescending(taskItem => taskItem.Priority)
            .ThenBy(taskItem => taskItem.DueAtUtc ?? DateTimeOffset.MaxValue)
            .ThenByDescending(taskItem => taskItem.CreatedAtUtc)
            .Select(taskItem => new TaskItemDto(
                taskItem.Id,
                taskItem.Title,
                taskItem.Description,
                (TaskItemStatusDto)taskItem.Status,
                (TaskPriorityDto)taskItem.Priority,
                taskItem.DueAtUtc,
                taskItem.PlannedFor,
                taskItem.CreatedAtUtc,
                taskItem.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return taskItems;
    }

    public async Task<TaskItemDto?> GetTaskItemByIdAsync(Guid taskItemId, CancellationToken cancellationToken = default)
    {
        TaskItemDto? taskItem = await noahDbContext.TaskItems
            .AsNoTracking()
            .Where(taskItem => taskItem.Id == taskItemId)
            .Select(taskItem => new TaskItemDto(
                taskItem.Id,
                taskItem.Title,
                taskItem.Description,
                (TaskItemStatusDto)taskItem.Status,
                (TaskPriorityDto)taskItem.Priority,
                taskItem.DueAtUtc,
                taskItem.PlannedFor,
                taskItem.CreatedAtUtc,
                taskItem.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return taskItem;
    }

    public async Task<TaskItemDto> CreateTaskItemAsync(
        CreateTaskItemRequest request,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset currentDateTimeUtc = DateTimeOffset.UtcNow;

        TaskItem taskItem = new()
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Description = NormalizeOptionalText(request.Description),
            Status = TaskItemStatus.Open,
            Priority = (TaskPriority)request.Priority,
            DueAtUtc = request.DueAtUtc,
            PlannedFor = request.PlannedFor,
            CreatedAtUtc = currentDateTimeUtc,
            UpdatedAtUtc = null
        };

        noahDbContext.TaskItems.Add(taskItem);

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(taskItem);
    }

    public async Task<TaskItemDto?> UpdateTaskItemAsync(
        Guid taskItemId,
        UpdateTaskItemRequest request,
        CancellationToken cancellationToken = default)
    {
        TaskItem? taskItem = await noahDbContext.TaskItems
            .FirstOrDefaultAsync(taskItem => taskItem.Id == taskItemId, cancellationToken);

        if (taskItem == null)
        {
            return null;
        }

        taskItem.Title = request.Title.Trim();
        taskItem.Description = NormalizeOptionalText(request.Description, taskItem.Description);
        taskItem.Status = (TaskItemStatus)request.Status;
        taskItem.Priority = (TaskPriority)request.Priority;
        taskItem.DueAtUtc = request.DueAtUtc;
        taskItem.PlannedFor = request.PlannedFor;
        taskItem.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(taskItem);
    }

    public async Task<bool> DeleteTaskItemAsync(Guid taskItemId, CancellationToken cancellationToken = default)
    {
        TaskItem? taskItem = await noahDbContext.TaskItems
            .FirstOrDefaultAsync(taskItem => taskItem.Id == taskItemId, cancellationToken);

        if (taskItem == null)
        {
            return false;
        }

        noahDbContext.TaskItems.Remove(taskItem);

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static string? NormalizeOptionalText(string? value, string? fallbackValue = "")
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallbackValue
            : value.Trim();
    }

    private static TaskItemDto MapToDto(TaskItem taskItem)
    {
        return new TaskItemDto(
            taskItem.Id,
            taskItem.Title,
            taskItem.Description,
            (TaskItemStatusDto)taskItem.Status,
            (TaskPriorityDto)taskItem.Priority,
            taskItem.DueAtUtc,
            taskItem.PlannedFor,
            taskItem.CreatedAtUtc,
            taskItem.UpdatedAtUtc);
    }
}
