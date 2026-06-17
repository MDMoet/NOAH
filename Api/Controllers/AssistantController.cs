using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NOAH.Contracts.Assistant;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

/// <summary>
/// Exposes endpoints for assistant messaging, chats, settings, and long-term memory.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AssistantController(
    IAssistantService assistantService,
    IAssistantSettingsService assistantSettingsService,
    IAssistantChatService assistantChatService,
    IAssistantMemoryService assistantMemoryService,
    ILogger<AssistantController> logger)
    : ControllerBase
{
    /// <summary>
    /// Processes a user message through the assistant.
    /// </summary>
    /// <param name="request">The assistant command request to process.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The assistant response and interaction metadata.</returns>
    [HttpPost("message")]
    public async Task<ActionResult<AssistantCommandResponse>> ProcessMessageAsync(
        [FromBody] AssistantCommandRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = await ValidateAssistantCommandAsync(
            request,
            cancellationToken);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        AssistantCommandResponse assistantCommandResponse =
            await assistantService.ProcessMessageAsync(request!, cancellationToken);

        logger.LogInformation("Processed message with interaction id {InteractionId}.", assistantCommandResponse.InteractionId);

        return Ok(assistantCommandResponse);
    }

    /// <summary>
    /// Gets the persisted assistant settings.
    /// </summary>
    [HttpGet("settings")]
    public async Task<ActionResult<AssistantSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken)
    {
        AssistantSettingsDto assistantSettings = await assistantSettingsService.GetSettingsAsync(cancellationToken);
        return Ok(assistantSettings);
    }

    /// <summary>
    /// Updates the persisted assistant settings.
    /// </summary>
    [HttpPut("settings")]
    public async Task<ActionResult<AssistantSettingsDto>> UpdateSettingsAsync(
        [FromBody] UpdateAssistantSettingsRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateUpdateSettingsRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        AssistantSettingsDto assistantSettings =
            await assistantSettingsService.UpdateSettingsAsync(request!, cancellationToken);

        logger.LogInformation("Updated assistant settings {SettingsId}.", assistantSettings.Id);

        return Ok(assistantSettings);
    }

    /// <summary>
    /// Gets all assistant chat threads ordered by latest activity.
    /// </summary>
    [HttpGet("chats")]
    public async Task<ActionResult<IReadOnlyList<AssistantChatDto>>> GetChatsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AssistantChatDto> chats = await assistantChatService.GetChatsAsync(cancellationToken);
        return Ok(chats);
    }

    /// <summary>
    /// Creates a new assistant chat thread.
    /// </summary>
    [HttpPost("chats")]
    public async Task<ActionResult<AssistantChatDto>> CreateChatAsync(
        [FromBody] CreateAssistantChatRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateCreateChatRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        AssistantChatDto chat = await assistantChatService.CreateChatAsync(request!, cancellationToken);

        logger.LogInformation("Created assistant chat {ChatId}.", chat.Id);

        return CreatedAtRoute(
            "GetAssistantChatById",
            new { chatId = chat.Id },
            chat);
    }

    /// <summary>
    /// Gets one assistant chat by id.
    /// </summary>
    [HttpGet("chats/{chatId:guid}", Name = "GetAssistantChatById")]
    public async Task<ActionResult<AssistantChatDto>> GetChatByIdAsync(
        Guid chatId,
        CancellationToken cancellationToken)
    {
        if (chatId == Guid.Empty)
        {
            return BadRequest("Chat id cannot be empty.");
        }

        AssistantChatDto? chat = await assistantChatService.GetChatByIdAsync(chatId, cancellationToken);

        if (chat == null)
        {
            return NotFound();
        }

        return Ok(chat);
    }

    /// <summary>
    /// Updates the editable metadata of an assistant chat.
    /// </summary>
    [HttpPatch("chats/{chatId:guid}")]
    public async Task<ActionResult<AssistantChatDto>> UpdateChatAsync(
        Guid chatId,
        [FromBody] UpdateAssistantChatRequest? request,
        CancellationToken cancellationToken)
    {
        if (chatId == Guid.Empty)
        {
            return BadRequest("Chat id cannot be empty.");
        }

        Dictionary<string, string[]> validationErrors = ValidateUpdateChatRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        AssistantChatDto? updatedChat = await assistantChatService.UpdateChatAsync(
            chatId,
            request!,
            cancellationToken);

        if (updatedChat == null)
        {
            return NotFound();
        }

        logger.LogInformation("Updated assistant chat {ChatId}.", chatId);

        return Ok(updatedChat);
    }

    /// <summary>
    /// Deletes an assistant chat and its scoped messages.
    /// </summary>
    [HttpDelete("chats/{chatId:guid}")]
    public async Task<IActionResult> DeleteChatAsync(
        Guid chatId,
        CancellationToken cancellationToken)
    {
        if (chatId == Guid.Empty)
        {
            return BadRequest("Chat id cannot be empty.");
        }

        bool chatWasDeleted = await assistantChatService.DeleteChatAsync(chatId, cancellationToken);

        if (!chatWasDeleted)
        {
            return NotFound();
        }

        logger.LogInformation("Deleted assistant chat {ChatId}.", chatId);

        return NoContent();
    }

    /// <summary>
    /// Gets recent messages for one assistant chat thread.
    /// </summary>
    [HttpGet("chats/{chatId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<AssistantInteractionDto>>> GetChatMessagesAsync(
        Guid chatId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (chatId == Guid.Empty)
        {
            return BadRequest("Chat id cannot be empty.");
        }

        if (!await assistantChatService.ExistsAsync(chatId, cancellationToken))
        {
            return NotFound();
        }

        IReadOnlyList<AssistantInteractionDto> messages =
            await assistantChatService.GetMessagesAsync(chatId, take, cancellationToken);

        return Ok(messages);
    }

    /// <summary>
    /// Sends a new message through a specific assistant chat thread.
    /// </summary>
    [HttpPost("chats/{chatId:guid}/messages")]
    public async Task<ActionResult<AssistantCommandResponse>> ProcessChatMessageAsync(
        Guid chatId,
        [FromBody] AssistantCommandRequest? request,
        CancellationToken cancellationToken)
    {
        if (chatId == Guid.Empty)
        {
            return BadRequest("Chat id cannot be empty.");
        }

        if (!await assistantChatService.ExistsAsync(chatId, cancellationToken))
        {
            return NotFound();
        }

        Dictionary<string, string[]> validationErrors = await ValidateAssistantCommandAsync(
            request,
            cancellationToken,
            skipChatValidation: true);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        AssistantCommandResponse assistantCommandResponse = await assistantService.ProcessMessageAsync(
            request! with { ChatId = chatId },
            cancellationToken);

        logger.LogInformation(
            "Processed assistant chat message for chat {ChatId} with interaction id {InteractionId}.",
            chatId,
            assistantCommandResponse.InteractionId);

        return Ok(assistantCommandResponse);
    }

    /// <summary>
    /// Gets all long-term assistant memory items.
    /// </summary>
    [HttpGet("memory")]
    public async Task<ActionResult<IReadOnlyList<AssistantMemoryItemDto>>> GetMemoryItemsAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AssistantMemoryItemDto> memoryItems =
            await assistantMemoryService.GetMemoryItemsAsync(cancellationToken);

        return Ok(memoryItems);
    }

    /// <summary>
    /// Gets one long-term assistant memory item by id.
    /// </summary>
    [HttpGet("memory/{memoryItemId:guid}", Name = "GetAssistantMemoryItemById")]
    public async Task<ActionResult<AssistantMemoryItemDto>> GetMemoryItemByIdAsync(
        Guid memoryItemId,
        CancellationToken cancellationToken)
    {
        if (memoryItemId == Guid.Empty)
        {
            return BadRequest("Memory item id cannot be empty.");
        }

        AssistantMemoryItemDto? memoryItem =
            await assistantMemoryService.GetMemoryItemByIdAsync(memoryItemId, cancellationToken);

        if (memoryItem == null)
        {
            return NotFound();
        }

        return Ok(memoryItem);
    }

    /// <summary>
    /// Creates a new long-term assistant memory item.
    /// </summary>
    [HttpPost("memory")]
    public async Task<ActionResult<AssistantMemoryItemDto>> CreateMemoryItemAsync(
        [FromBody] CreateAssistantMemoryItemRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateCreateMemoryItemRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        AssistantMemoryItemDto memoryItem =
            await assistantMemoryService.CreateMemoryItemAsync(request!, cancellationToken);

        logger.LogInformation("Created assistant memory item {MemoryItemId}.", memoryItem.Id);

        return CreatedAtRoute(
            "GetAssistantMemoryItemById",
            new { memoryItemId = memoryItem.Id },
            memoryItem);
    }

    /// <summary>
    /// Updates an existing long-term assistant memory item.
    /// </summary>
    [HttpPut("memory/{memoryItemId:guid}")]
    public async Task<ActionResult<AssistantMemoryItemDto>> UpdateMemoryItemAsync(
        Guid memoryItemId,
        [FromBody] UpdateAssistantMemoryItemRequest? request,
        CancellationToken cancellationToken)
    {
        if (memoryItemId == Guid.Empty)
        {
            return BadRequest("Memory item id cannot be empty.");
        }

        Dictionary<string, string[]> validationErrors = ValidateUpdateMemoryItemRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        AssistantMemoryItemDto? memoryItem = await assistantMemoryService.UpdateMemoryItemAsync(
            memoryItemId,
            request!,
            cancellationToken);

        if (memoryItem == null)
        {
            return NotFound();
        }

        logger.LogInformation("Updated assistant memory item {MemoryItemId}.", memoryItemId);

        return Ok(memoryItem);
    }

    /// <summary>
    /// Deletes a long-term assistant memory item.
    /// </summary>
    [HttpDelete("memory/{memoryItemId:guid}")]
    public async Task<IActionResult> DeleteMemoryItemAsync(
        Guid memoryItemId,
        CancellationToken cancellationToken)
    {
        if (memoryItemId == Guid.Empty)
        {
            return BadRequest("Memory item id cannot be empty.");
        }

        bool memoryItemWasDeleted = await assistantMemoryService.DeleteMemoryItemAsync(
            memoryItemId,
            cancellationToken);

        if (!memoryItemWasDeleted)
        {
            return NotFound();
        }

        logger.LogInformation("Deleted assistant memory item {MemoryItemId}.", memoryItemId);

        return NoContent();
    }

    /// <summary>
    /// Validates an assistant command request.
    /// </summary>
    private async Task<Dictionary<string, string[]>> ValidateAssistantCommandAsync(
        AssistantCommandRequest? request,
        CancellationToken cancellationToken,
        bool skipChatValidation = false)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        if (string.IsNullOrWhiteSpace(request.Input))
        {
            validationErrors[nameof(request.Input)] = ["Input is required."];
        }

        if (!Enum.IsDefined(request.InputMode))
        {
            validationErrors[nameof(request.InputMode)] = ["Input mode is invalid."];
        }

        if (request.PreferredResponseMode.HasValue && !Enum.IsDefined(request.PreferredResponseMode.Value))
        {
            validationErrors[nameof(request.PreferredResponseMode)] = ["Preferred response mode is invalid."];
        }

        if (!skipChatValidation &&
            request.ChatId.HasValue &&
            request.ChatId.Value == Guid.Empty)
        {
            validationErrors[nameof(request.ChatId)] = ["Chat id cannot be empty."];
        }

        if (!skipChatValidation &&
            request.ChatId.HasValue &&
            request.ChatId.Value != Guid.Empty &&
            !await assistantChatService.ExistsAsync(request.ChatId.Value, cancellationToken))
        {
            validationErrors[nameof(request.ChatId)] = ["Chat was not found."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Validates an assistant settings update request.
    /// </summary>
    private static Dictionary<string, string[]> ValidateUpdateSettingsRequest(UpdateAssistantSettingsRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        if (!Enum.IsDefined(request.PreferredResponseMode))
        {
            validationErrors[nameof(request.PreferredResponseMode)] = ["Preferred response mode is invalid."];
        }

        if (string.IsNullOrWhiteSpace(request.SpeechCulture))
        {
            validationErrors[nameof(request.SpeechCulture)] = ["Speech culture is required."];
        }

        if (request.ConversationMemoryMessageCount is < 0 or > 20)
        {
            validationErrors[nameof(request.ConversationMemoryMessageCount)] =
                ["Conversation memory message count must be between 0 and 20."];
        }

        if (request.LongTermMemoryItemCount is < 0 or > 20)
        {
            validationErrors[nameof(request.LongTermMemoryItemCount)] =
                ["Long-term memory item count must be between 0 and 20."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Validates a create-chat request.
    /// </summary>
    private static Dictionary<string, string[]> ValidateCreateChatRequest(CreateAssistantChatRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Validates a chat update request.
    /// </summary>
    private static Dictionary<string, string[]> ValidateUpdateChatRequest(UpdateAssistantChatRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        if (request.Title == null &&
            request.Description == null &&
            !request.IsArchived.HasValue)
        {
            validationErrors["Request"] = ["At least one field must be provided."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Validates a create-memory request.
    /// </summary>
    private static Dictionary<string, string[]> ValidateCreateMemoryItemRequest(CreateAssistantMemoryItemRequest? request)
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

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            validationErrors[nameof(request.Content)] = ["Content is required."];
        }

        if (request.SourceInteractionId.HasValue && request.SourceInteractionId.Value == Guid.Empty)
        {
            validationErrors[nameof(request.SourceInteractionId)] = ["Source interaction id cannot be empty."];
        }

        if (request.SourceChatId.HasValue && request.SourceChatId.Value == Guid.Empty)
        {
            validationErrors[nameof(request.SourceChatId)] = ["Source chat id cannot be empty."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Validates a memory-item update request.
    /// </summary>
    private static Dictionary<string, string[]> ValidateUpdateMemoryItemRequest(UpdateAssistantMemoryItemRequest? request)
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

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            validationErrors[nameof(request.Content)] = ["Content is required."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Converts validation errors into ASP.NET model state.
    /// </summary>
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
