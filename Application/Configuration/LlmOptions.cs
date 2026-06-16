namespace Application.Configuration;

/// <summary>
/// Binds the configured language-model endpoints and routing defaults.
/// </summary>
public sealed class LlmOptions
{
    /// <summary>
    /// The configuration section name used to bind LLM settings.
    /// </summary>
    public const string SectionName = "Llm";

    /// <summary>
    /// Gets or sets the default model key used when no other route wins.
    /// </summary>
    public string DefaultModelKey { get; set; } = "general";

    /// <summary>
    /// Gets or sets the model key reserved for coding-focused requests.
    /// </summary>
    public string CodingModelKey { get; set; } = "coding";

    /// <summary>
    /// Gets or sets the inactivity timeout for the coding model session.
    /// </summary>
    public int CodingInactivityTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Gets or sets the configured model endpoints keyed by logical model name.
    /// </summary>
    public Dictionary<string, LlmModelOptions> Models { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Describes one configured chat-completions endpoint.
/// </summary>
public sealed class LlmModelOptions
{
    /// <summary>
    /// Gets or sets whether the model can be selected for requests.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the base URL of the OpenAI-compatible server.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the provider model name sent in the chat-completions payload.
    /// </summary>
    public string Model { get; set; } = "default";

    /// <summary>
    /// Gets or sets the maximum completion tokens requested from the model.
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Gets or sets the model temperature used for completion requests.
    /// </summary>
    public double Temperature { get; set; } = 0.4;

    /// <summary>
    /// Gets or sets the per-request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the optional API key used for bearer authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the optional authorization header name.
    /// </summary>
    public string? AuthorizationHeaderName { get; set; }

    /// <summary>
    /// Gets or sets the optional authorization header value.
    /// </summary>
    public string? AuthorizationHeaderValue { get; set; }

    /// <summary>
    /// Gets or sets local process-management settings for this model endpoint.
    /// </summary>
    public LlmModelProcessOptions Process { get; set; } = new();
}

/// <summary>
/// Describes how NOAH should manage a local llama-server process for one model.
/// </summary>
public sealed class LlmModelProcessOptions
{
    /// <summary>
    /// Gets or sets whether NOAH should manage the model process locally.
    /// </summary>
    public bool Managed { get; set; }

    /// <summary>
    /// Gets or sets whether the model should be started automatically when the app boots.
    /// </summary>
    public bool AutoStart { get; set; }

    /// <summary>
    /// Gets or sets whether the model should be kept alive whenever it is not being displaced by an exclusive model.
    /// </summary>
    public bool KeepAlive { get; set; }

    /// <summary>
    /// Gets or sets whether other managed models should be stopped while this model is active.
    /// </summary>
    public bool StopOtherModelsWhileActive { get; set; }

    /// <summary>
    /// Gets or sets whether the model should be stopped after its inactivity window elapses.
    /// </summary>
    public bool StopOnInactivity { get; set; }

    /// <summary>
    /// Gets or sets the process launcher used to start the model.
    /// </summary>
    public string Launcher { get; set; } = "PowerShell";

    /// <summary>
    /// Gets or sets the optional PowerShell executable path used for Start-Process launches.
    /// </summary>
    public string? PowerShellExecutablePath { get; set; }

    /// <summary>
    /// Gets or sets the llama-server executable path.
    /// </summary>
    public string? ServerExecutablePath { get; set; }

    /// <summary>
    /// Gets or sets the GGUF model file path.
    /// </summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Gets or sets the optional working directory used when launching the model process.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the host interface passed to llama-server.
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets the local port passed to llama-server.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// Gets or sets the context size passed to llama-server.
    /// </summary>
    public int ContextSize { get; set; } = 131074;

    /// <summary>
    /// Gets or sets the value passed to the llama-server GPU layers argument.
    /// </summary>
    public string GpuLayers { get; set; } = "all";

    /// <summary>
    /// Gets or sets extra arguments appended after the standard llama-server arguments.
    /// </summary>
    public string[] AdditionalArguments { get; set; } = [];

    /// <summary>
    /// Gets or sets how long NOAH should wait for the model endpoint to become ready.
    /// </summary>
    public int ReadinessTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the poll interval used while waiting for the model endpoint to become ready.
    /// </summary>
    public int StartupPollIntervalMilliseconds { get; set; } = 1000;
}
