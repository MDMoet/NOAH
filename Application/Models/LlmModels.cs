namespace Application.Models;

/// <summary>
/// Represents one chat-completions request sent to a configured LLM endpoint.
/// </summary>
public sealed record LlmChatCompletionRequest(
    string PrimaryModelKey,
    string Prompt,
    string SystemPrompt,
    IReadOnlyList<string> FallbackModelKeys,
    LlmStructuredOutputRequest? StructuredOutput = null,
    int? MaxTokensOverride = null);

/// <summary>
/// Describes an optional structured-output request for OpenAI-compatible providers.
/// </summary>
public sealed record LlmStructuredOutputRequest(
    string SchemaName,
    string SchemaJson,
    bool Strict = true);

/// <summary>
/// Represents the parsed result of one chat-completions call.
/// </summary>
public sealed record LlmChatCompletionResult(
    string ModelKey,
    string ProviderModel,
    string ResponseText,
    string? FinishReason,
    bool UsedFallback,
    LlmTokenUsage? Usage);

/// <summary>
/// Represents token-usage metadata returned by a chat-completions provider.
/// </summary>
public sealed record LlmTokenUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);

/// <summary>
/// Represents the health status of one configured LLM endpoint.
/// </summary>
public sealed record LlmModelHealthStatus(
    string ModelKey,
    bool IsEnabled,
    bool IsHealthy,
    string Message,
    string? ProviderModel,
    TimeSpan Duration);
