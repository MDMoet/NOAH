using Application.Models;

namespace Application.Interfaces;

/// <summary>
/// Tracks assistant model-session activity and manages local model processes.
/// </summary>
public interface IAssistantModelProcessManager
{
    /// <summary>
    /// Records activity for the supplied model key.
    /// </summary>
    /// <param name="modelKey">The logical model key that handled or was selected for work.</param>
    /// <param name="occurredAtUtc">The UTC timestamp of the activity.</param>
    void RecordActivity(string modelKey, DateTimeOffset occurredAtUtc);

    /// <summary>
    /// Ensures the default managed model is running.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task EnsureDefaultModelRunningAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the selected managed model is running and applies any configured exclusivity rules.
    /// </summary>
    /// <param name="modelKey">The logical model key to prepare.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task EnsureModelReadyAsync(string modelKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current session status for the supplied model key.
    /// </summary>
    /// <param name="modelKey">The logical model key to inspect.</param>
    /// <param name="currentTimeUtc">The current UTC timestamp used for inactivity evaluation.</param>
    /// <returns>The model session status snapshot.</returns>
    AssistantModelSessionStatus GetSessionStatus(string modelKey, DateTimeOffset currentTimeUtc);

    /// <summary>
    /// Reconciles managed model processes against the configured lifecycle policy.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task ReconcileProcessesAsync(CancellationToken cancellationToken = default);
}
