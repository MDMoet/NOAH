using Application.Models;
using NOAH.Contracts.Assistant;

namespace Application.Interfaces;

/// <summary>
/// Builds free-form prompts for the configured language model.
/// </summary>
public interface IAssistantPromptBuilder
{
    /// <summary>
    /// Builds a complete prompt from the user request and available NOAH context.
    /// </summary>
    /// <param name="request">The assistant command request.</param>
    /// <param name="context">The context available to the assistant.</param>
    /// <returns>The prompt to send to the language model.</returns>
    string BuildPrompt(AssistantCommandRequest request, AssistantPromptContext context);
}
