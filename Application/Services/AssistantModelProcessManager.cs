using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using Application.Configuration;
using Application.Interfaces;
using Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services;

/// <summary>
/// Tracks logical model sessions and manages local llama-server processes for configured models.
/// </summary>
public sealed class AssistantModelProcessManager(
    IOptionsMonitor<LlmOptions> llmOptionsMonitor,
    TimeProvider timeProvider,
    ILogger<AssistantModelProcessManager> logger)
    : IAssistantModelProcessManager
{
    private const string CudaBinPath = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin";

    // Use a small dedicated client here so readiness polling stays cheap and independent from
    // normal LLM completion traffic.
    private static readonly HttpClient ReadinessHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastActivityByModelKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ManagedModelProcessState> _managedProcessStateByModelKey =
        new(StringComparer.OrdinalIgnoreCase);

    // Managed model start/stop operations are serialized to avoid race conditions between the
    // background reconciler and foreground request-driven warmup.
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    /// Records activity for the supplied model key.
    /// </summary>
    /// <param name="modelKey">The logical model key that was used.</param>
    /// <param name="occurredAtUtc">The UTC timestamp of the activity.</param>
    public void RecordActivity(string modelKey, DateTimeOffset occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            return;
        }

        _lastActivityByModelKey[modelKey] = occurredAtUtc.ToUniversalTime();
    }

    /// <summary>
    /// Ensures the default managed model is running.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task EnsureDefaultModelRunningAsync(CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        await _lifecycleGate.WaitAsync(cancellationToken);

        try
        {
            LlmOptions llmOptions = llmOptionsMonitor.CurrentValue;
            string defaultModelKey = ResolveDefaultModelKey(llmOptions);

            if (!TryGetManagedModelConfiguration(defaultModelKey, llmOptions, out _,
                    out LlmModelProcessOptions? processOptions) ||
                !processOptions.AutoStart)
            {
                logger.LogDebug(
                    "Skipped default model auto-start in {ElapsedMs} ms because the default managed model is not configured for auto-start.",
                    GetElapsedMilliseconds(stopwatch));
                return;
            }

            await EnsureModelReadyCoreAsync(defaultModelKey, llmOptions, cancellationToken);

            logger.LogInformation(
                "Ensured default managed model {ModelKey} is running in {ElapsedMs} ms.",
                defaultModelKey,
                GetElapsedMilliseconds(stopwatch));
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Ensures the selected managed model is running and applies any configured exclusivity rules.
    /// </summary>
    /// <param name="modelKey">The logical model key to prepare.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task EnsureModelReadyAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        await _lifecycleGate.WaitAsync(cancellationToken);

        try
        {
            await EnsureModelReadyCoreAsync(
                modelKey,
                llmOptionsMonitor.CurrentValue,
                cancellationToken);

            logger.LogInformation(
                "Ensured managed model {ModelKey} is ready in {ElapsedMs} ms.",
                modelKey,
                GetElapsedMilliseconds(stopwatch));
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Gets the current session status for the supplied model key.
    /// </summary>
    /// <param name="modelKey">The logical model key to inspect.</param>
    /// <param name="currentTimeUtc">The current UTC timestamp.</param>
    /// <returns>The current session status for the model.</returns>
    public AssistantModelSessionStatus GetSessionStatus(string modelKey, DateTimeOffset currentTimeUtc)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            return new AssistantModelSessionStatus(
                string.Empty,
                false,
                null,
                "No model key was supplied.");
        }

        DateTimeOffset normalizedCurrentTimeUtc = currentTimeUtc.ToUniversalTime();
        LlmOptions llmOptions = llmOptionsMonitor.CurrentValue;
        bool isCodingModel = string.Equals(
            modelKey,
            llmOptions.CodingModelKey,
            StringComparison.OrdinalIgnoreCase);

        if (!isCodingModel || llmOptions.CodingInactivityTimeoutMinutes <= 0)
        {
            _lastActivityByModelKey.TryGetValue(modelKey, out DateTimeOffset lastActivityUtc);

            return new AssistantModelSessionStatus(
                modelKey,
                true,
                lastActivityUtc == default ? null : lastActivityUtc,
                "No inactivity policy is applied.");
        }

        if (!_lastActivityByModelKey.TryGetValue(modelKey, out DateTimeOffset recordedActivityUtc))
        {
            return new AssistantModelSessionStatus(
                modelKey,
                false,
                null,
                "The coding session is idle.");
        }

        TimeSpan inactivityWindow = TimeSpan.FromMinutes(llmOptions.CodingInactivityTimeoutMinutes);
        bool isActive = normalizedCurrentTimeUtc - recordedActivityUtc <= inactivityWindow;

        return new AssistantModelSessionStatus(
            modelKey,
            isActive,
            recordedActivityUtc,
            isActive
                ? "The coding session is active."
                : "The coding session is inactive because the inactivity window elapsed.");
    }

    /// <summary>
    /// Reconciles managed model processes against the configured lifecycle policy.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task ReconcileProcessesAsync(CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        await _lifecycleGate.WaitAsync(cancellationToken);

        try
        {
            LlmOptions llmOptions = llmOptionsMonitor.CurrentValue;
            string codingModelKey = llmOptions.CodingModelKey;
            AssistantModelSessionStatus codingSessionStatus =
                GetSessionStatus(codingModelKey, timeProvider.GetUtcNow());

            if (TryGetManagedModelConfiguration(
                    codingModelKey,
                    llmOptions,
                    out LlmModelOptions? codingModelOptions,
                    out LlmModelProcessOptions? codingProcessOptions) &&
                codingProcessOptions.StopOnInactivity &&
                !codingSessionStatus.IsActive &&
                await IsModelProcessRunningAsync(codingModelKey, codingModelOptions, codingProcessOptions,
                    cancellationToken))
            {
                await StopManagedModelCoreAsync(
                    codingModelKey,
                    codingModelOptions,
                    codingProcessOptions,
                    "The coding inactivity window elapsed.",
                    cancellationToken);
            }

            if (TryGetManagedModelConfiguration(
                    codingModelKey,
                    llmOptions,
                    out codingModelOptions,
                    out codingProcessOptions) &&
                codingProcessOptions.StopOtherModelsWhileActive &&
                codingSessionStatus.IsActive &&
                await IsModelProcessRunningAsync(codingModelKey, codingModelOptions, codingProcessOptions,
                    cancellationToken))
            {
                await StopConflictingModelsCoreAsync(
                    codingModelKey,
                    llmOptions,
                    cancellationToken);
                return;
            }

            string defaultModelKey = ResolveDefaultModelKey(llmOptions);

            if (TryGetManagedModelConfiguration(defaultModelKey, llmOptions, out _,
                    out LlmModelProcessOptions? defaultProcessOptions) &&
                (defaultProcessOptions.AutoStart || defaultProcessOptions.KeepAlive))
            {
                await EnsureModelReadyCoreAsync(defaultModelKey, llmOptions, cancellationToken);
            }

            logger.LogInformation(
                "Reconciled managed LLM processes in {ElapsedMs} ms.",
                GetElapsedMilliseconds(stopwatch));
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task EnsureModelReadyCoreAsync(
        string modelKey,
        LlmOptions llmOptions,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        if (!TryGetManagedModelConfiguration(modelKey, llmOptions, out LlmModelOptions? modelOptions,
                out LlmModelProcessOptions? processOptions))
        {
            logger.LogDebug(
                "Skipped ensure-model-ready for {ModelKey} because no managed configuration was found.",
                modelKey);
            return;
        }

        Stopwatch conflictStopwatch = Stopwatch.StartNew();
        await StopConflictingModelsCoreAsync(modelKey, llmOptions, cancellationToken);
        logger.LogInformation(
            "Conflict resolution for managed model {ModelKey} completed in {ElapsedMs} ms.",
            modelKey,
            GetElapsedMilliseconds(conflictStopwatch));

        Stopwatch endpointCheckStopwatch = Stopwatch.StartNew();
        if (await IsModelEndpointReadyAsync(modelOptions, cancellationToken))
        {
            int? portProcessId =
                await TryGetListeningProcessIdAsync(GetPort(modelOptions, processOptions), cancellationToken);

            if (portProcessId.HasValue)
            {
                _managedProcessStateByModelKey[modelKey] = new ManagedModelProcessState(
                    portProcessId.Value,
                    _managedProcessStateByModelKey.TryGetValue(modelKey, out ManagedModelProcessState? existingState)
                        ? existingState.StartedAtUtc
                        : timeProvider.GetUtcNow());
            }

            logger.LogInformation(
                "Managed model {ModelKey} was already ready. Endpoint check took {EndpointElapsedMs} ms. Total ensure time: {TotalElapsedMs} ms.",
                modelKey,
                GetElapsedMilliseconds(endpointCheckStopwatch),
                GetElapsedMilliseconds(stopwatch));
            return;
        }

        ValidateManagedProcessConfiguration(modelKey, processOptions);

        Stopwatch startProcessStopwatch = Stopwatch.StartNew();
        int processId = await StartModelProcessAsync(modelKey, modelOptions, processOptions, cancellationToken);
        logger.LogInformation(
            "Started managed model process for {ModelKey} in {ElapsedMs} ms.",
            modelKey,
            GetElapsedMilliseconds(startProcessStopwatch));

        _managedProcessStateByModelKey[modelKey] = new ManagedModelProcessState(
            processId,
            timeProvider.GetUtcNow());

        Stopwatch readinessStopwatch = Stopwatch.StartNew();
        await WaitForEndpointReadinessAsync(modelKey, modelOptions, processOptions, cancellationToken);

        logger.LogInformation(
            "Managed model {ModelKey} became ready after startup. Readiness wait: {ReadinessElapsedMs} ms. Total ensure time: {TotalElapsedMs} ms.",
            modelKey,
            GetElapsedMilliseconds(readinessStopwatch),
            GetElapsedMilliseconds(stopwatch));
    }

    private async Task StopConflictingModelsCoreAsync(
        string targetModelKey,
        LlmOptions llmOptions,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedModelConfiguration(
                targetModelKey,
                llmOptions,
                out LlmModelOptions? targetModelOptions,
                out LlmModelProcessOptions? targetProcessOptions))
        {
            return;
        }

        foreach ((string candidateModelKey, LlmModelOptions candidateModelOptions) in llmOptions.Models)
        {
            if (string.Equals(candidateModelKey, targetModelKey, StringComparison.OrdinalIgnoreCase) ||
                !candidateModelOptions.Process.Managed)
            {
                continue;
            }

            LlmModelProcessOptions candidateProcessOptions = candidateModelOptions.Process;
            bool hasConflict = targetProcessOptions.StopOtherModelsWhileActive ||
                               candidateProcessOptions.StopOtherModelsWhileActive;

            if (!hasConflict ||
                !await IsModelProcessRunningAsync(
                    candidateModelKey,
                    candidateModelOptions,
                    candidateProcessOptions,
                    cancellationToken))
            {
                continue;
            }

            await StopManagedModelCoreAsync(
                candidateModelKey,
                candidateModelOptions,
                candidateProcessOptions,
                $"Model {targetModelKey} requires exclusive access.",
                cancellationToken);
        }
    }

    private async Task<bool> IsModelProcessRunningAsync(
        string modelKey,
        LlmModelOptions modelOptions,
        LlmModelProcessOptions processOptions,
        CancellationToken cancellationToken)
    {
        if (_managedProcessStateByModelKey.TryGetValue(modelKey, out ManagedModelProcessState? managedState) &&
            TryGetRunningProcess(managedState.ProcessId, out _))
        {
            return true;
        }

        if (await IsModelEndpointReadyAsync(modelOptions, cancellationToken))
        {
            int? processId =
                await TryGetListeningProcessIdAsync(GetPort(modelOptions, processOptions), cancellationToken);

            if (processId.HasValue)
            {
                _managedProcessStateByModelKey[modelKey] = new ManagedModelProcessState(
                    processId.Value,
                    _managedProcessStateByModelKey.TryGetValue(modelKey, out managedState)
                        ? managedState.StartedAtUtc
                        : timeProvider.GetUtcNow());
            }

            return true;
        }

        _managedProcessStateByModelKey.TryRemove(modelKey, out _);
        return false;
    }

    private async Task StopManagedModelCoreAsync(
        string modelKey,
        LlmModelOptions modelOptions,
        LlmModelProcessOptions processOptions,
        string reason,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        int? processId =
            _managedProcessStateByModelKey.TryGetValue(modelKey, out ManagedModelProcessState? managedState)
                ? managedState.ProcessId
                : await TryGetListeningProcessIdAsync(GetPort(modelOptions, processOptions), cancellationToken);

        if (!processId.HasValue)
        {
            logger.LogInformation(
                "Skipped stopping model {ModelKey} because no running process could be resolved. Reason: {Reason}",
                modelKey,
                reason);
            _managedProcessStateByModelKey.TryRemove(modelKey, out _);
            return;
        }

        if (!TryGetRunningProcess(processId.Value, out Process? process))
        {
            _managedProcessStateByModelKey.TryRemove(modelKey, out _);
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);

            logger.LogInformation(
                "Stopped managed model {ModelKey} (PID {ProcessId}) in {ElapsedMs} ms. Reason: {Reason}",
                modelKey,
                processId.Value,
                GetElapsedMilliseconds(stopwatch),
                reason);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to stop managed model {ModelKey} (PID {ProcessId}) after {ElapsedMs} ms.",
                modelKey,
                processId.Value,
                GetElapsedMilliseconds(stopwatch));
        }
        finally
        {
            process.Dispose();
            _managedProcessStateByModelKey.TryRemove(modelKey, out _);
        }
    }

    private async Task<int> StartModelProcessAsync(
        string modelKey,
        LlmModelOptions modelOptions,
        LlmModelProcessOptions processOptions,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string launcher = processOptions.Launcher.Trim();

        logger.LogInformation(
            "Starting managed model {ModelKey} using launcher {Launcher}. Endpoint: {BaseUrl}",
            modelKey,
            launcher,
            modelOptions.BaseUrl);

        if (string.Equals(launcher, "Direct", StringComparison.OrdinalIgnoreCase))
        {
            int directProcessId = StartDirectProcess(modelKey, modelOptions, processOptions);
            logger.LogInformation(
                "Managed model {ModelKey} launcher {Launcher} returned PID {ProcessId} in {ElapsedMs} ms.",
                modelKey,
                launcher,
                directProcessId,
                GetElapsedMilliseconds(stopwatch));
            return directProcessId;
        }

        int powerShellProcessId = await StartPowerShellProcessAsync(modelKey, modelOptions, processOptions, cancellationToken);
        logger.LogInformation(
            "Managed model {ModelKey} launcher {Launcher} returned PID {ProcessId} in {ElapsedMs} ms.",
            modelKey,
            launcher,
            powerShellProcessId,
            GetElapsedMilliseconds(stopwatch));
        return powerShellProcessId;
    }

    private int StartDirectProcess(
        string modelKey,
        LlmModelOptions modelOptions,
        LlmModelProcessOptions processOptions)
    {
        ProcessStartInfo processStartInfo = new(processOptions.ServerExecutablePath!)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = ResolveWorkingDirectory(processOptions)
        };

        ApplyCudaEnvironment(processStartInfo);

        foreach (string argument in BuildLlamaServerArguments(modelOptions, processOptions))
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        Process? process = Process.Start(processStartInfo);

        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start managed model {modelKey}.");
        }

        logger.LogInformation(
            "Started managed model {ModelKey} directly with PID {ProcessId}.",
            modelKey,
            process.Id);

        return process.Id;
    }

    private async Task<int> StartPowerShellProcessAsync(
        string modelKey,
        LlmModelOptions modelOptions,
        LlmModelProcessOptions processOptions,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string powerShellExecutablePath = string.IsNullOrWhiteSpace(processOptions.PowerShellExecutablePath)
            ? "powershell.exe"
            : processOptions.PowerShellExecutablePath;

        string script = BuildPowerShellLaunchScript(modelOptions, processOptions);

        ProcessStartInfo processStartInfo = new(powerShellExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ResolveWorkingDirectory(processOptions)
        };

        ApplyCudaEnvironment(processStartInfo);

        processStartInfo.ArgumentList.Add("-NoProfile");
        processStartInfo.ArgumentList.Add("-Command");
        processStartInfo.ArgumentList.Add(script);

        using Process process = Process.Start(processStartInfo)
                                ?? throw new InvalidOperationException(
                                    $"Failed to launch PowerShell for managed model {modelKey}.");

        string standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string standardError = await process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"PowerShell failed to start managed model {modelKey}. {standardError.Trim()}");
        }

        string? lastOutputLine = standardOutput
            .Trim()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        if (!int.TryParse(
                lastOutputLine,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int processId))
        {
            throw new InvalidOperationException(
                $"PowerShell did not return a valid PID for managed model {modelKey}. Output: {standardOutput.Trim()}");
        }

        logger.LogInformation(
            "Started managed model {ModelKey} through PowerShell with PID {ProcessId} in {ElapsedMs} ms.",
            modelKey,
            processId,
            GetElapsedMilliseconds(stopwatch));

        return processId;
    }

    private async Task WaitForEndpointReadinessAsync(
        string modelKey,
        LlmModelOptions modelOptions,
        LlmModelProcessOptions processOptions,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan readinessTimeout = TimeSpan.FromSeconds(Math.Max(processOptions.ReadinessTimeoutSeconds, 1));
        TimeSpan pollInterval =
            TimeSpan.FromMilliseconds(Math.Max(processOptions.StartupPollIntervalMilliseconds, 250));
        DateTimeOffset deadlineUtc = timeProvider.GetUtcNow().Add(readinessTimeout);
        int pollCount = 0;

        while (timeProvider.GetUtcNow() < deadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pollCount++;

            if (await IsModelEndpointReadyAsync(modelOptions, cancellationToken))
            {
                logger.LogInformation(
                    "Managed model {ModelKey} is ready at {BaseUrl} after {ElapsedMs} ms and {PollCount} poll(s).",
                    modelKey,
                    modelOptions.BaseUrl,
                    GetElapsedMilliseconds(stopwatch),
                    pollCount);
                return;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        logger.LogWarning(
            "Managed model {ModelKey} did not become ready after {ElapsedMs} ms and {PollCount} poll(s). Timeout: {TimeoutSeconds} second(s).",
            modelKey,
            GetElapsedMilliseconds(stopwatch),
            pollCount,
            readinessTimeout.TotalSeconds);

        throw new TimeoutException(
            $"Managed model {modelKey} did not become ready within {readinessTimeout.TotalSeconds:0} seconds.");
    }

    private static void ValidateManagedProcessConfiguration(
        string modelKey,
        LlmModelProcessOptions processOptions)
    {
        if (string.IsNullOrWhiteSpace(processOptions.ServerExecutablePath))
        {
            throw new InvalidOperationException(
                $"Managed model {modelKey} is missing a server executable path.");
        }

        if (string.IsNullOrWhiteSpace(processOptions.ModelPath))
        {
            throw new InvalidOperationException(
                $"Managed model {modelKey} is missing a model path.");
        }
    }

    private static bool TryGetManagedModelConfiguration(
        string modelKey,
        LlmOptions llmOptions,
        [NotNullWhen(true)] out LlmModelOptions? modelOptions,
        [NotNullWhen(true)] out LlmModelProcessOptions? processOptions)
    {
        modelOptions = null;
        processOptions = null;

        if (string.IsNullOrWhiteSpace(modelKey) ||
            !llmOptions.Models.TryGetValue(modelKey, out LlmModelOptions? resolvedModelOptions) ||
            !resolvedModelOptions.Enabled ||
            !resolvedModelOptions.Process.Managed)
        {
            return false;
        }

        modelOptions = resolvedModelOptions;
        processOptions = resolvedModelOptions.Process;
        return true;
    }

    private static string ResolveDefaultModelKey(LlmOptions llmOptions)
    {
        if (!string.IsNullOrWhiteSpace(llmOptions.DefaultModelKey))
        {
            return llmOptions.DefaultModelKey;
        }

        foreach ((string modelKey, LlmModelOptions modelOptions) in llmOptions.Models)
        {
            if (modelOptions.Enabled)
            {
                return modelKey;
            }
        }

        throw new InvalidOperationException("No enabled LLM models are configured.");
    }

    private static int GetPort(LlmModelOptions modelOptions, LlmModelProcessOptions processOptions)
    {
        if (processOptions.Port.HasValue)
        {
            return processOptions.Port.Value;
        }

        if (!string.IsNullOrWhiteSpace(modelOptions.BaseUrl) &&
            Uri.TryCreate(modelOptions.BaseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            return baseUri.Port;
        }

        throw new InvalidOperationException("The managed model port could not be resolved.");
    }

    private async Task<bool> IsModelEndpointReadyAsync(
        LlmModelOptions modelOptions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelOptions.BaseUrl) ||
            !Uri.TryCreate(AppendTrailingSlash(modelOptions.BaseUrl), UriKind.Absolute, out Uri? baseUri))
        {
            return false;
        }

        foreach (string path in new[] { "health", "v1/models" })
        {
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUri, path));
                using HttpResponseMessage response = await ReadinessHttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
        }

        return false;
    }

    private async Task<int?> TryGetListeningProcessIdAsync(
        int port,
        CancellationToken cancellationToken)
    {
        string powerShellExecutablePath = "powershell.exe";
        string script =
            $"$connection = Get-NetTCPConnection -LocalPort {port.ToString(CultureInfo.InvariantCulture)} -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1; " +
            "if ($null -ne $connection) { Write-Output $connection.OwningProcess }";
        ProcessStartInfo processStartInfo = new(powerShellExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        processStartInfo.ArgumentList.Add("-NoProfile");
        processStartInfo.ArgumentList.Add("-Command");
        processStartInfo.ArgumentList.Add(script);

        using Process process = Process.Start(processStartInfo)
                                ?? throw new InvalidOperationException("Failed to query the listening model process.");

        string standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return int.TryParse(
            standardOutput.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int processId)
            ? processId
            : null;
    }

    private static bool TryGetRunningProcess(
        int processId,
        [NotNullWhen(true)] out Process? process)
    {
        process = null;

        try
        {
            Process resolvedProcess = Process.GetProcessById(processId);

            if (resolvedProcess.HasExited)
            {
                resolvedProcess.Dispose();
                return false;
            }

            process = resolvedProcess;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string[] BuildLlamaServerArguments(
        LlmModelOptions modelOptions,
        LlmModelProcessOptions processOptions)
    {
        List<string> arguments =
        [
            "-m",
            processOptions.ModelPath!,
            "--host",
            ResolveHost(modelOptions, processOptions),
            "--port",
            GetPort(modelOptions, processOptions).ToString(CultureInfo.InvariantCulture),
            "--ctx-size",
            processOptions.ContextSize.ToString(CultureInfo.InvariantCulture),
            "--n-gpu-layers",
            processOptions.GpuLayers
        ];

        arguments.AddRange(processOptions.AdditionalArguments);
        return arguments.ToArray();
    }

    private static string BuildPowerShellLaunchScript(
        LlmModelOptions modelOptions,
        LlmModelProcessOptions processOptions)
    {
        string[] arguments = BuildLlamaServerArguments(modelOptions, processOptions);

        string argumentList = string.Join(
            " ",
            arguments.Select(QuoteWindowsCommandLineArgument));

        StringBuilder scriptBuilder = new();

        scriptBuilder.Append("$serverPath = '");
        scriptBuilder.Append(EscapePowerShellSingleQuotedString(processOptions.ServerExecutablePath!));
        scriptBuilder.AppendLine("';");

        scriptBuilder.Append("$workingDirectory = '");
        scriptBuilder.Append(EscapePowerShellSingleQuotedString(ResolveWorkingDirectory(processOptions)));
        scriptBuilder.AppendLine("';");

        scriptBuilder.Append("$cudaBinPath = '");
        scriptBuilder.Append(EscapePowerShellSingleQuotedString(CudaBinPath));
        scriptBuilder.AppendLine("';");

        scriptBuilder.AppendLine("if (Test-Path -LiteralPath $cudaBinPath) { $env:Path = $cudaBinPath + ';' + $env:Path; }");

        scriptBuilder.Append("$argumentList = '");
        scriptBuilder.Append(EscapePowerShellSingleQuotedString(argumentList));
        scriptBuilder.AppendLine("';");

        scriptBuilder.AppendLine("$process = Start-Process -FilePath $serverPath -ArgumentList $argumentList -WorkingDirectory $workingDirectory -PassThru;");
        scriptBuilder.AppendLine("Write-Output $process.Id;");

        return scriptBuilder.ToString();
    }

    private static string ResolveWorkingDirectory(LlmModelProcessOptions processOptions)
    {
        if (!string.IsNullOrWhiteSpace(processOptions.WorkingDirectory))
        {
            return processOptions.WorkingDirectory;
        }

        return Path.GetDirectoryName(processOptions.ServerExecutablePath!)
               ?? Environment.CurrentDirectory;
    }

    private static string ResolveHost(LlmModelOptions modelOptions, LlmModelProcessOptions processOptions)
    {
        if (!string.IsNullOrWhiteSpace(processOptions.Host))
        {
            return processOptions.Host;
        }

        if (!string.IsNullOrWhiteSpace(modelOptions.BaseUrl) &&
            Uri.TryCreate(modelOptions.BaseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            return baseUri.Host;
        }

        return IPAddress.Loopback.ToString();
    }

    private static string AppendTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal)
            ? value
            : value + "/";
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static void ApplyCudaEnvironment(ProcessStartInfo processStartInfo)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(CudaBinPath))
        {
            return;
        }

        string pathKey = processStartInfo.Environment.Keys
                             .FirstOrDefault(key => string.Equals(key, "Path", StringComparison.OrdinalIgnoreCase))
                         ?? "Path";

        processStartInfo.Environment.TryGetValue(pathKey, out string? existingPath);

        if (PathAlreadyContainsCudaBin(existingPath))
        {
            return;
        }

        processStartInfo.Environment[pathKey] = string.IsNullOrWhiteSpace(existingPath)
            ? CudaBinPath
            : $"{CudaBinPath};{existingPath}";
    }

    private static bool PathAlreadyContainsCudaBin(string? existingPath)
    {
        if (string.IsNullOrWhiteSpace(existingPath))
        {
            return false;
        }

        return existingPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(path => string.Equals(
                path.TrimEnd('\\'),
                CudaBinPath.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase));
    }

    private static string QuoteWindowsCommandLineArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        if (!argument.Any(char.IsWhiteSpace) && !argument.Contains('"'))
        {
            return argument;
        }

        StringBuilder quotedArgumentBuilder = new();
        quotedArgumentBuilder.Append('"');

        int backslashCount = 0;

        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                quotedArgumentBuilder.Append('\\', backslashCount * 2 + 1);
                quotedArgumentBuilder.Append('"');
                backslashCount = 0;
                continue;
            }

            quotedArgumentBuilder.Append('\\', backslashCount);
            quotedArgumentBuilder.Append(character);
            backslashCount = 0;
        }

        quotedArgumentBuilder.Append('\\', backslashCount * 2);
        quotedArgumentBuilder.Append('"');

        return quotedArgumentBuilder.ToString();
    }

    private static double GetElapsedMilliseconds(Stopwatch stopwatch)
    {
        return Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
    }

    private sealed record ManagedModelProcessState(
        int ProcessId,
        DateTimeOffset StartedAtUtc);
}
