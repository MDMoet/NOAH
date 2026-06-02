using Api.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NOAH.Contracts.Tasks;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TasksController(ITasksService tasksService, ILogger<TasksController> logger)
    : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TaskItemDto>> CreateTaskItemAsync(
        [FromBody] CreateTaskItemRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateCreateTaskItemRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        TaskItemDto createdTaskItem = await tasksService.CreateTaskItemAsync(request!, cancellationToken);

        logger.LogInformation("Created task item with id {TaskItemId}.", createdTaskItem.Id);

        return CreatedAtAction(
            "GetTaskItemById",
            new { taskItemId = createdTaskItem.Id },
            createdTaskItem);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaskItemDto>>> GetTaskItemsAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TaskItemDto> taskItems = await tasksService.GetTaskItemsAsync(cancellationToken);

        return Ok(taskItems);
    }

    [HttpGet("{taskItemId:guid}", Name = "GetTaskItemById")]
    public async Task<ActionResult<TaskItemDto>> GetTaskItemByIdAsync(
        Guid taskItemId,
        CancellationToken cancellationToken)
    {
        if (taskItemId == Guid.Empty)
        {
            return BadRequest("Task item id cannot be empty.");
        }

        TaskItemDto? taskItem = await tasksService.GetTaskItemByIdAsync(taskItemId, cancellationToken);

        if (taskItem == null)
        {
            return NotFound();
        }

        return Ok(taskItem);
    }

    [HttpPut("{taskItemId:guid}")]
    public async Task<ActionResult<TaskItemDto>> UpdateTaskItemAsync(
        Guid taskItemId,
        [FromBody] UpdateTaskItemRequest? request,
        CancellationToken cancellationToken)
    {
        if (taskItemId == Guid.Empty)
        {
            return BadRequest("Task item id cannot be empty.");
        }

        Dictionary<string, string[]> validationErrors = ValidateUpdateTaskItemRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        TaskItemDto? updatedTaskItem = await tasksService.UpdateTaskItemAsync(
            taskItemId,
            request!,
            cancellationToken);

        if (updatedTaskItem == null)
        {
            return NotFound();
        }

        logger.LogInformation("Updated task item with id {TaskItemId}.", taskItemId);

        return Ok(updatedTaskItem);
    }

    [HttpDelete("{taskItemId:guid}")]
    public async Task<IActionResult> DeleteTaskItemAsync(
        Guid taskItemId,
        CancellationToken cancellationToken)
    {
        if (taskItemId == Guid.Empty)
        {
            return BadRequest("Task item id cannot be empty.");
        }

        bool taskItemWasDeleted = await tasksService.DeleteTaskItemAsync(taskItemId, cancellationToken);

        if (!taskItemWasDeleted)
        {
            return NotFound();
        }

        logger.LogInformation("Deleted task item with id {TaskItemId}.", taskItemId);

        return NoContent();
    }

    private static Dictionary<string, string[]> ValidateCreateTaskItemRequest(CreateTaskItemRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            validationErrors[nameof(request.Title)] = ["Title is required."];
        }

        if (!Enum.IsDefined(request.Priority))
        {
            validationErrors[nameof(request.Priority)] = ["Priority is invalid."];
        }

        return validationErrors;
    }

    private static Dictionary<string, string[]> ValidateUpdateTaskItemRequest(UpdateTaskItemRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            validationErrors[nameof(request.Title)] = ["Title is required."];
        }

        if (!Enum.IsDefined(request.Status))
        {
            validationErrors[nameof(request.Status)] = ["Status is invalid."];
        }

        if (!Enum.IsDefined(request.Priority))
        {
            validationErrors[nameof(request.Priority)] = ["Priority is invalid."];
        }

        return validationErrors;
    }

    private static ModelStateDictionary ToModelStateDictionary(
        Dictionary<string, string[]> validationErrors)
    {
        ModelStateDictionary modelStateDictionary = new();

        foreach (KeyValuePair<string, string[]> validationError in validationErrors)
        {
            foreach (string errorMessage in validationError.Value)
            {
                modelStateDictionary.AddModelError(validationError.Key, errorMessage);
            }
        }

        return modelStateDictionary;
    }
}
