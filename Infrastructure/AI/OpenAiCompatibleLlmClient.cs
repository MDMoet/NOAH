using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Configuration;
using Application.Interfaces;
using Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI;

/// <summary>
/// Calls OpenAI-compatible chat-completions endpoints configured for NOAH.
/// </summary>
public sealed class OpenAiCompatibleLlmClient(
    HttpClient httpClient,
    IAssistantModelProcessManager assistantModelProcessManager,
    IOptionsMonitor<LlmOptions> llmOptionsMonitor,
    ILogger<OpenAiCompatibleLlmClient> logger)
    : ILlmClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Generates a completion using the selected primary model and configured fallbacks.
    /// </summary>
    /// <param name="request">The completion request to send.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The parsed completion result.</returns>
    public async Task<LlmChatCompletionResult> GenerateResponseAsync(
        LlmChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Stopwatch stopwatch = Stopwatch.StartNew();
        List<string> errorMessages = [];
        List<string> candidateModelKeys = BuildCandidateModelKeys(request);

        logger.LogInformation(
            "Starting LLM completion with primary model {PrimaryModelKey}. Candidates: {CandidateCount}. Structured output: {UsesStructuredOutput}. Prompt length: {PromptLength}.",
            request.PrimaryModelKey,
            candidateModelKeys.Count,
            request.StructuredOutput != null,
            request.Prompt.Length);

        foreach ((string modelKey, int index) in candidateModelKeys.Select((value, index) => (value, index)))
        {
            ModelAttemptResult modelAttemptResult = await TryGenerateForModelAsync(
                modelKey,
                request,
                usedFallback: index > 0,
                trackActivity: true,
                cancellationToken);

            if (modelAttemptResult.CompletionResult != null)
            {
                logger.LogInformation(
                    "LLM completion succeeded in {ElapsedMs} ms after {AttemptCount} attempt(s). Selected model: {ModelKey}.",
                    GetElapsedMilliseconds(stopwatch),
                    index + 1,
                    modelAttemptResult.CompletionResult.ModelKey);
                return modelAttemptResult.CompletionResult;
            }

            if (!string.IsNullOrWhiteSpace(modelAttemptResult.ErrorMessage))
            {
                errorMessages.Add($"{modelKey}: {modelAttemptResult.ErrorMessage}");
            }
        }

        logger.LogWarning(
            "LLM completion failed after {ElapsedMs} ms. Candidate count: {CandidateCount}. Errors: {Errors}",
            GetElapsedMilliseconds(stopwatch),
            candidateModelKeys.Count,
            string.Join(" | ", errorMessages.DefaultIfEmpty("No usable models were configured.")));

        throw new InvalidOperationException(
            $"All configured LLM attempts failed. {string.Join(" | ", errorMessages.DefaultIfEmpty("No usable models were configured."))}");
    }

    /// <summary>
    /// Checks the health of all configured model endpoints.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The per-model health status snapshots.</returns>
    public async Task<IReadOnlyList<LlmModelHealthStatus>> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        LlmOptions llmOptions = llmOptionsMonitor.CurrentValue;
        List<LlmModelHealthStatus> healthStatuses = [];

        foreach ((string modelKey, LlmModelOptions modelOptions) in llmOptions.Models)
        {
            if (!modelOptions.Enabled)
            {
                healthStatuses.Add(new LlmModelHealthStatus(
                    modelKey,
                    false,
                    false,
                    "The model is disabled.",
                    modelOptions.Model,
                    TimeSpan.Zero));
                continue;
            }

            if (string.IsNullOrWhiteSpace(modelOptions.BaseUrl))
            {
                healthStatuses.Add(new LlmModelHealthStatus(
                    modelKey,
                    true,
                    false,
                    "The model has no configured base URL.",
                    modelOptions.Model,
                    TimeSpan.Zero));
                continue;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                ModelAttemptResult modelAttemptResult = await TryGenerateForModelAsync(
                    modelKey,
                    new LlmChatCompletionRequest(
                        modelKey,
                        "Reply with OK.",
                        "You are a health probe. Respond with OK.",
                        Array.Empty<string>()),
                    usedFallback: false,
                    trackActivity: false,
                    cancellationToken);

                stopwatch.Stop();

                if (modelAttemptResult.CompletionResult != null)
                {
                    healthStatuses.Add(new LlmModelHealthStatus(
                        modelKey,
                        true,
                        true,
                        "The model endpoint responded successfully.",
                        modelAttemptResult.CompletionResult.ProviderModel,
                        stopwatch.Elapsed));
                }
                else
                {
                    healthStatuses.Add(new LlmModelHealthStatus(
                        modelKey,
                        true,
                        false,
                        modelAttemptResult.ErrorMessage ?? "The model health probe failed.",
                        modelOptions.Model,
                        stopwatch.Elapsed));
                }
            }
            catch (Exception exception)
            {
                stopwatch.Stop();

                logger.LogWarning(
                    exception,
                    "Health probe failed for model {ModelKey}.",
                    modelKey);

                healthStatuses.Add(new LlmModelHealthStatus(
                    modelKey,
                    true,
                    false,
                    exception.Message,
                    modelOptions.Model,
                    stopwatch.Elapsed));
            }
        }

        return healthStatuses;
    }

    private async Task<ModelAttemptResult> TryGenerateForModelAsync(
        string modelKey,
        LlmChatCompletionRequest request,
        bool usedFallback,
        bool trackActivity,
        CancellationToken cancellationToken)
    {
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        LlmOptions llmOptions = llmOptionsMonitor.CurrentValue;

        if (!llmOptions.Models.TryGetValue(modelKey, out LlmModelOptions? modelOptions))
        {
            return new ModelAttemptResult(null, "The configured model key was not found.");
        }

        if (!modelOptions.Enabled)
        {
            return new ModelAttemptResult(null, "The model is disabled.");
        }

        if (string.IsNullOrWhiteSpace(modelOptions.BaseUrl))
        {
            return new ModelAttemptResult(null, "The model base URL is missing.");
        }

        if (trackActivity)
        {
            assistantModelProcessManager.RecordActivity(modelKey, DateTimeOffset.UtcNow);
        }

        Stopwatch readinessStopwatch = Stopwatch.StartNew();
        await assistantModelProcessManager.EnsureModelReadyAsync(
            modelKey,
            cancellationToken);
        readinessStopwatch.Stop();

        if (trackActivity)
        {
            assistantModelProcessManager.RecordActivity(modelKey, DateTimeOffset.UtcNow);
        }

        Uri requestUri = BuildChatCompletionsUri(modelOptions.BaseUrl);
        OpenAiChatCompletionRequest payload = BuildPayload(request, modelOptions);
        string serializedPayload = JsonSerializer.Serialize(payload, SerializerOptions);

        using HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(serializedPayload, Encoding.UTF8, "application/json")
        };

        ApplyAuthorization(httpRequestMessage.Headers, modelOptions);

        using CancellationTokenSource timeoutCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(modelOptions.TimeoutSeconds, 1)));

        Stopwatch httpStopwatch = Stopwatch.StartNew();

        logger.LogInformation(
            "Starting LLM model attempt for {ModelKey}. Fallback: {UsedFallback}. Structured output: {UsesStructuredOutput}. Max tokens: {MaxTokens}. Readiness completed in {ReadinessElapsedMs} ms.",
            modelKey,
            usedFallback,
            request.StructuredOutput != null,
            ResolveMaxTokens(request, modelOptions),
            GetElapsedMilliseconds(readinessStopwatch));

        try
        {
            using HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(
                httpRequestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCancellationTokenSource.Token);

            string rawResponse = await httpResponseMessage.Content.ReadAsStringAsync(timeoutCancellationTokenSource.Token);
            httpStopwatch.Stop();

            if (!httpResponseMessage.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "LLM request to model {ModelKey} failed with status {StatusCode}. Readiness: {ReadinessElapsedMs} ms. HTTP: {HttpElapsedMs} ms. Total: {TotalElapsedMs} ms. Body: {ResponseBody}",
                    modelKey,
                    (int)httpResponseMessage.StatusCode,
                    GetElapsedMilliseconds(readinessStopwatch),
                    GetElapsedMilliseconds(httpStopwatch),
                    GetElapsedMilliseconds(totalStopwatch),
                    TruncateForLog(rawResponse, 4000));

                return new ModelAttemptResult(
                    null,
                    $"The model returned HTTP {(int)httpResponseMessage.StatusCode}.");
            }

            OpenAiChatCompletionResponse? completionResponse;

            try
            {
                completionResponse = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(
                    rawResponse,
                    SerializerOptions);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(
                    exception,
                    "LLM response from model {ModelKey} could not be parsed as JSON. Readiness: {ReadinessElapsedMs} ms. HTTP: {HttpElapsedMs} ms. Total: {TotalElapsedMs} ms. Raw response: {ResponseBody}",
                    modelKey,
                    GetElapsedMilliseconds(readinessStopwatch),
                    GetElapsedMilliseconds(httpStopwatch),
                    GetElapsedMilliseconds(totalStopwatch),
                    TruncateForLog(rawResponse, 4000));

                return new ModelAttemptResult(
                    null,
                    "The model response was not valid JSON.");
            }

            if (completionResponse?.Choices == null || completionResponse.Choices.Count == 0)
            {
                logger.LogWarning(
                    "LLM response from model {ModelKey} did not contain choices. Readiness: {ReadinessElapsedMs} ms. HTTP: {HttpElapsedMs} ms. Total: {TotalElapsedMs} ms. Raw response: {ResponseBody}",
                    modelKey,
                    GetElapsedMilliseconds(readinessStopwatch),
                    GetElapsedMilliseconds(httpStopwatch),
                    GetElapsedMilliseconds(totalStopwatch),
                    TruncateForLog(rawResponse, 4000));

                return new ModelAttemptResult(
                    null,
                    "The model response did not contain any completion choices.");
            }

            OpenAiChatChoice completionChoice = completionResponse.Choices[0];
            
            if (string.Equals(completionChoice.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                string reasoningText = ExtractJsonElementText(completionChoice.Message?.ReasoningContent);
                bool hasReasoningContent = !string.IsNullOrWhiteSpace(reasoningText);

                logger.LogWarning(
                    "LLM response from model {ModelKey} hit the max token limit before producing a complete final answer. Readiness: {ReadinessElapsedMs} ms. HTTP: {HttpElapsedMs} ms. Total: {TotalElapsedMs} ms. Has reasoning content: {HasReasoningContent}.",
                    modelKey,
                    GetElapsedMilliseconds(readinessStopwatch),
                    GetElapsedMilliseconds(httpStopwatch),
                    GetElapsedMilliseconds(totalStopwatch),
                    hasReasoningContent);

                return new ModelAttemptResult(
                    null,
                    hasReasoningContent
                        ? "The model hit the max token limit while producing reasoning content and did not complete a final answer."
                        : "The model hit the max token limit before completing its response.");
            }

            string responseText = ExtractChoiceText(completionChoice);

            if (string.IsNullOrWhiteSpace(responseText))
            {
                string reasoningText = ExtractJsonElementText(completionChoice.Message?.ReasoningContent);
                bool hasReasoningContent = !string.IsNullOrWhiteSpace(reasoningText);

                logger.LogWarning(
                    "LLM response from model {ModelKey} did not contain usable text. Readiness: {ReadinessElapsedMs} ms. HTTP: {HttpElapsedMs} ms. Total: {TotalElapsedMs} ms. Finish reason: {FinishReason}. Has reasoning content: {HasReasoningContent}.",
                    modelKey,
                    GetElapsedMilliseconds(readinessStopwatch),
                    GetElapsedMilliseconds(httpStopwatch),
                    GetElapsedMilliseconds(totalStopwatch),
                    completionChoice.FinishReason ?? "unknown",
                    hasReasoningContent);

                return new ModelAttemptResult(
                    null,
                    hasReasoningContent
                        ? "The model returned reasoning content but no final text content."
                        : "The model response did not contain usable text content.");
            }

            if (trackActivity)
            {
                assistantModelProcessManager.RecordActivity(modelKey, DateTimeOffset.UtcNow);
            }

            logger.LogInformation(
                "LLM request to model {ModelKey} completed. Readiness: {ReadinessElapsedMs} ms. HTTP: {HttpElapsedMs} ms. Total: {TotalElapsedMs} ms. Provider model: {ProviderModel}. Fallback used: {UsedFallback}. Prompt tokens: {PromptTokens}. Completion tokens: {CompletionTokens}.",
                modelKey,
                GetElapsedMilliseconds(readinessStopwatch),
                GetElapsedMilliseconds(httpStopwatch),
                GetElapsedMilliseconds(totalStopwatch),
                completionResponse.Model ?? modelOptions.Model,
                usedFallback,
                completionResponse.Usage?.PromptTokens,
                completionResponse.Usage?.CompletionTokens);

            return new ModelAttemptResult(
                new LlmChatCompletionResult(
                    modelKey,
                    completionResponse.Model ?? modelOptions.Model,
                    responseText.Trim(),
                    completionChoice.FinishReason,
                    usedFallback,
                    completionResponse.Usage == null
                        ? null
                        : new LlmTokenUsage(
                            completionResponse.Usage.PromptTokens,
                            completionResponse.Usage.CompletionTokens,
                            completionResponse.Usage.TotalTokens)),
                null);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            httpStopwatch.Stop();

            logger.LogWarning(
                exception,
                "LLM request to model {ModelKey} timed out. Readiness: {ReadinessElapsedMs} ms. HTTP: {HttpElapsedMs} ms. Total: {TotalElapsedMs} ms.",
                modelKey,
                GetElapsedMilliseconds(readinessStopwatch),
                GetElapsedMilliseconds(httpStopwatch),
                GetElapsedMilliseconds(totalStopwatch));

            return new ModelAttemptResult(null, "The model request timed out.");
        }
        catch (Exception exception)
        {
            httpStopwatch.Stop();

            logger.LogWarning(
                exception,
                "LLM request to model {ModelKey} failed. Readiness: {ReadinessElapsedMs} ms. HTTP: {HttpElapsedMs} ms. Total: {TotalElapsedMs} ms.",
                modelKey,
                GetElapsedMilliseconds(readinessStopwatch),
                GetElapsedMilliseconds(httpStopwatch),
                GetElapsedMilliseconds(totalStopwatch));

            return new ModelAttemptResult(null, exception.Message);
        }
    }

    private static List<string> BuildCandidateModelKeys(LlmChatCompletionRequest request)
    {
        List<string> candidateModelKeys = [];

        void AddCandidate(string? modelKey)
        {
            if (string.IsNullOrWhiteSpace(modelKey) ||
                candidateModelKeys.Contains(modelKey, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            candidateModelKeys.Add(modelKey);
        }

        AddCandidate(request.PrimaryModelKey);

        foreach (string fallbackModelKey in request.FallbackModelKeys)
        {
            AddCandidate(fallbackModelKey);
        }

        return candidateModelKeys;
    }

    private static OpenAiChatCompletionRequest BuildPayload(
        LlmChatCompletionRequest request,
        LlmModelOptions modelOptions)
    {
        List<OpenAiChatMessageRequest> messages = [];

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new OpenAiChatMessageRequest("system", request.SystemPrompt));
        }
        
        // Make sure /no_think is passed for Qwen-style thinking models to skip reasoning to reduce token usage
        string userPrompt = request.StructuredOutput == null
            ? request.Prompt
            : "/no_think\n" + request.Prompt;

        messages.Add(new OpenAiChatMessageRequest("user", userPrompt));

        object? responseFormat = null;

        if (request.StructuredOutput != null)
        {
            JsonElement schemaElement = JsonSerializer.Deserialize<JsonElement>(request.StructuredOutput.SchemaJson);

            responseFormat = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = request.StructuredOutput.SchemaName,
                    strict = request.StructuredOutput.Strict,
                    schema = schemaElement
                }
            };
        }

        return new OpenAiChatCompletionRequest(
            modelOptions.Model,
            messages,
            ResolveMaxTokens(request, modelOptions),
            modelOptions.Temperature,
            responseFormat);
    }

    private static int ResolveMaxTokens(
        LlmChatCompletionRequest request,
        LlmModelOptions modelOptions)
    {
        return Math.Max(request.MaxTokensOverride ?? modelOptions.MaxTokens, 1);
    }

    private static void ApplyAuthorization(HttpRequestHeaders headers, LlmModelOptions modelOptions)
    {
        if (!string.IsNullOrWhiteSpace(modelOptions.AuthorizationHeaderName) &&
            !string.IsNullOrWhiteSpace(modelOptions.AuthorizationHeaderValue))
        {
            headers.TryAddWithoutValidation(
                modelOptions.AuthorizationHeaderName,
                modelOptions.AuthorizationHeaderValue);
            return;
        }

        if (!string.IsNullOrWhiteSpace(modelOptions.ApiKey))
        {
            headers.Authorization = new AuthenticationHeaderValue("Bearer", modelOptions.ApiKey);
        }
    }

    private static Uri BuildChatCompletionsUri(string baseUrl)
    {
        Uri normalizedBaseUri = new(AppendTrailingSlash(baseUrl), UriKind.Absolute);
        return new Uri(normalizedBaseUri, "v1/chat/completions");
    }

    private static string AppendTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal)
            ? value
            : value + "/";
    }

    private static string ExtractChoiceText(OpenAiChatChoice completionChoice)
    {
        string messageText = ExtractMessageText(completionChoice.Message);

        if (!string.IsNullOrWhiteSpace(messageText))
        {
            return messageText;
        }

        string choiceText = ExtractJsonElementText(completionChoice.Text);

        if (!string.IsNullOrWhiteSpace(choiceText))
        {
            return choiceText;
        }

        return string.Empty;
    }

    private static string ExtractMessageText(OpenAiChatMessage? message)
    {
        if (message == null)
        {
            return string.Empty;
        }

        string contentText = ExtractJsonElementText(message.Content);

        if (!string.IsNullOrWhiteSpace(contentText))
        {
            return contentText;
        }

        return string.Empty;
    }

    // OpenAI-compatible providers sometimes return content as:
    // - string
    // - array of text parts
    // - object with text/content
    private static string ExtractJsonElementText(JsonElement? contentElement)
    {
        if (contentElement == null ||
            contentElement.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return string.Empty;
        }

        JsonElement content = contentElement.Value;

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind == JsonValueKind.Number ||
            content.ValueKind == JsonValueKind.True ||
            content.ValueKind == JsonValueKind.False)
        {
            return content.ToString();
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            StringBuilder stringBuilder = new();

            foreach (JsonElement arrayElement in content.EnumerateArray())
            {
                string textPart = ExtractJsonElementText(arrayElement);

                if (string.IsNullOrWhiteSpace(textPart))
                {
                    continue;
                }

                if (stringBuilder.Length > 0)
                {
                    stringBuilder.AppendLine();
                }

                stringBuilder.Append(textPart);
            }

            return stringBuilder.Length > 0
                ? stringBuilder.ToString()
                : content.GetRawText();
        }

        if (content.ValueKind == JsonValueKind.Object)
        {
            foreach (string propertyName in new[] { "text", "content", "output_text" })
            {
                if (!content.TryGetProperty(propertyName, out JsonElement property))
                {
                    continue;
                }

                string text = ExtractJsonElementText(property);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return content.GetRawText();
        }

        return string.Empty;
    }

    private static string TruncateForLog(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }

    private static double GetElapsedMilliseconds(Stopwatch stopwatch)
    {
        return Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
    }

    private sealed record ModelAttemptResult(
        LlmChatCompletionResult? CompletionResult,
        string? ErrorMessage);

    private sealed record OpenAiChatCompletionRequest(
        [property: JsonPropertyName("model")]
        string Model,
        [property: JsonPropertyName("messages")]
        IReadOnlyList<OpenAiChatMessageRequest> Messages,
        [property: JsonPropertyName("max_tokens")]
        int MaxTokens,
        [property: JsonPropertyName("temperature")]
        double Temperature,
        [property: JsonPropertyName("response_format")]
        object? ResponseFormat);

    private sealed record OpenAiChatMessageRequest(
        [property: JsonPropertyName("role")]
        string Role,
        [property: JsonPropertyName("content")]
        string Content);

    private sealed class OpenAiChatCompletionResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("choices")]
        public List<OpenAiChatChoice> Choices { get; set; } = [];

        [JsonPropertyName("usage")]
        public OpenAiChatUsage? Usage { get; set; }
    }

    private sealed class OpenAiChatChoice
    {
        [JsonPropertyName("message")]
        public OpenAiChatMessage? Message { get; set; }

        [JsonPropertyName("text")]
        public JsonElement? Text { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private sealed class OpenAiChatMessage
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public JsonElement? Content { get; set; }

        [JsonPropertyName("reasoning_content")]
        public JsonElement? ReasoningContent { get; set; }

        [JsonPropertyName("tool_calls")]
        public JsonElement? ToolCalls { get; set; }
    }

    private sealed class OpenAiChatUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int? TotalTokens { get; set; }
    }
}
