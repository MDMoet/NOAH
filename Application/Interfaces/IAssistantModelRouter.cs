using Application.Models;
using NOAH.Contracts.Assistant;

namespace Application.Interfaces;

/// <summary>
/// Selects the best configured LLM for an assistant request.
/// </summary>
public interface IAssistantModelRouter
{
    /// <summary>
    /// Creates a heuristic model-routing decision for the supplied request.
    /// </summary>
    /// <param name="request">The assistant request that needs model routing.</param>
    /// <returns>The selected primary model, fallback order, and system prompt.</returns>
    AssistantModelRoutingDecision Route(AssistantCommandRequest request);
}
