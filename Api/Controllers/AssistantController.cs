using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NOAH.Contracts.Assistant;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

/// <summary>
/// Exposes endpoints for sending messages to the NOAH assistant.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AssistantController(IAssistantService assistantService,ILogger<AssistantController> logger)
    : ControllerBase
{
    /// <summary>
    /// Processes a user message through the assistant.
    /// </summary>
    /// <param name="request">The assistant command request to process.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The assistant response and interaction metadata.</returns>
    [HttpPost("message")]
    public async Task<ActionResult<AssistantCommandResponse>> CreateNoteAsync(
        [FromBody] AssistantCommandRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateAssistantCommand(request);

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
    /// Validates an assistant command request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateAssistantCommand(AssistantCommandRequest? request)
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

        if (request.PreferredResponseMode == null)
        {
            validationErrors[nameof(request.InputMode)] = ["Preferred response is required."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Converts validation errors into ASP.NET model state.
    /// </summary>
    /// <param name="validationErrors">The validation errors to convert.</param>
    /// <returns>The model state containing the validation errors.</returns>
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
