using Application.Configuration;
using Application.Interfaces;
using Application.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>
/// Probes configured assistant LLM endpoints and reports routing-session context.
/// </summary>
public sealed class LlmHealthCheck(
    ILlmClient llmClient,
    IAssistantModelProcessManager assistantModelProcessManager,
    IOptionsMonitor<LlmOptions> llmOptionsMonitor,
    TimeProvider timeProvider)
    : IHealthCheck
{
    /// <summary>
    /// Checks configured assistant model endpoints.
    /// </summary>
    /// <param name="context">The current health-check context.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The aggregated assistant LLM health result.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LlmModelHealthStatus> modelHealthStatuses =
            await llmClient.CheckHealthAsync(cancellationToken);
        LlmOptions llmOptions = llmOptionsMonitor.CurrentValue;
        DateTimeOffset currentTimeUtc = timeProvider.GetUtcNow();
        Dictionary<string, object> healthData = new(StringComparer.OrdinalIgnoreCase);

        foreach (LlmModelHealthStatus modelHealthStatus in modelHealthStatuses)
        {
            AssistantModelSessionStatus sessionStatus =
                assistantModelProcessManager.GetSessionStatus(modelHealthStatus.ModelKey, currentTimeUtc);

            healthData[modelHealthStatus.ModelKey] = new
            {
                modelHealthStatus.IsEnabled,
                modelHealthStatus.IsHealthy,
                modelHealthStatus.Message,
                modelHealthStatus.ProviderModel,
                latencyMs = Math.Round(modelHealthStatus.Duration.TotalMilliseconds, 2),
                sessionStatus.IsActive,
                sessionStatus.LastActivityAtUtc,
                sessionStatus.Reason
            };
        }

        bool hasHealthyEnabledModel = modelHealthStatuses.Any(modelHealthStatus =>
            modelHealthStatus.IsEnabled && modelHealthStatus.IsHealthy);
        bool defaultModelHealthy = modelHealthStatuses.Any(modelHealthStatus =>
            modelHealthStatus.IsEnabled &&
            modelHealthStatus.IsHealthy &&
            string.Equals(
                modelHealthStatus.ModelKey,
                llmOptions.DefaultModelKey,
                StringComparison.OrdinalIgnoreCase));

        HealthStatus overallStatus = !hasHealthyEnabledModel
            ? HealthStatus.Unhealthy
            : defaultModelHealthy
                ? HealthStatus.Healthy
                : HealthStatus.Degraded;

        string description = overallStatus switch
        {
            HealthStatus.Healthy => "The default assistant model is healthy.",
            HealthStatus.Degraded => "Fallback assistant models are healthy, but the default model is unavailable.",
            _ => "No healthy enabled assistant models are available."
        };

        return new HealthCheckResult(
            overallStatus,
            description,
            data: healthData);
    }
}
