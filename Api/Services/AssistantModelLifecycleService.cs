using Application.Interfaces;

namespace Api.Services;

/// <summary>
/// Starts and reconciles managed assistant model processes in the background.
/// </summary>
public sealed class AssistantModelLifecycleService(
    IAssistantModelProcessManager assistantModelProcessManager,
    ILogger<AssistantModelLifecycleService> logger)
    : BackgroundService
{
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs the managed model lifecycle loop.
    /// </summary>
    /// <param name="stoppingToken">Token used to stop the service.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await assistantModelProcessManager.EnsureDefaultModelRunningAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to start the default managed assistant model during application startup.");
        }

        using PeriodicTimer timer = new(ReconciliationInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await assistantModelProcessManager.ReconcileProcessesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Managed assistant model reconciliation failed.");
            }
        }
    }
}
