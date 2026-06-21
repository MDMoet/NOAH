using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client.Configuration;
using Client.Models;
using Microsoft.Maui.Storage;

namespace Client.Services;

public sealed class AssistantApiSettingsService
{
    private const string SelectedChatPreferenceKey = "assistant_selected_chat_id";

    public AssistantClientSettings Load()
    {
        string selectedChatValue = Preferences.Default.Get(SelectedChatPreferenceKey, string.Empty);
        Guid? lastSelectedChatId = Guid.TryParse(selectedChatValue, out Guid parsedChatId)
            ? parsedChatId
            : null;

        return new AssistantClientSettings(
            NoahClientBuildConfiguration.ApiBaseUrl,
            NoahClientBuildConfiguration.ApiKey,
            lastSelectedChatId);
    }

    public AssistantClientSettings EnsureSeededDefaults()
    {
        AssistantClientSettings settings = Load();
        Save(settings);
        return settings;
    }

    public void Save(AssistantClientSettings settings)
    {
        if (settings.LastSelectedChatId.HasValue)
        {
            Preferences.Default.Set(SelectedChatPreferenceKey, settings.LastSelectedChatId.Value.ToString());
        }
        else
        {
            Preferences.Default.Remove(SelectedChatPreferenceKey);
        }
    }

    public void SaveSelectedChatId(Guid? chatId)
    {
        AssistantClientSettings current = Load();
        Save(current with { LastSelectedChatId = chatId });
    }

    public bool HasTrustedApiKeyConfigured()
    {
        return !string.IsNullOrWhiteSpace(Load().ApiKey);
    }
}

public sealed class AssistantApiService(
    HttpClient httpClient,
    AssistantApiSettingsService settingsService,
    NoahAuthenticationService authenticationService,
    AppNavigationService navigationService)
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MessageRequestTimeout = TimeSpan.FromMinutes(3);

    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public Task<AssistantSettingsDto> GetAssistantSettingsAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<AssistantSettingsDto>(
            HttpMethod.Get,
            "assistant/settings",
            cancellationToken: cancellationToken,
            timeout: DefaultRequestTimeout);
    }

    public Task<AssistantSettingsDto> UpdateAssistantSettingsAsync(
        UpdateAssistantSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<AssistantSettingsDto>(
            HttpMethod.Put,
            "assistant/settings",
            request,
            cancellationToken: cancellationToken,
            timeout: DefaultRequestTimeout);
    }

    public Task<IReadOnlyList<AssistantChatDto>> GetChatsAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<IReadOnlyList<AssistantChatDto>>(
            HttpMethod.Get,
            "assistant/chats",
            cancellationToken: cancellationToken,
            timeout: DefaultRequestTimeout);
    }

    public Task<AssistantChatDto> CreateChatAsync(
        CreateAssistantChatRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<AssistantChatDto>(
            HttpMethod.Post,
            "assistant/chats",
            request,
            cancellationToken,
            DefaultRequestTimeout);
    }

    public Task<AssistantChatDto> UpdateChatAsync(
        Guid chatId,
        UpdateAssistantChatRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<AssistantChatDto>(
            HttpMethod.Patch,
            $"assistant/chats/{chatId:D}",
            request,
            cancellationToken,
            DefaultRequestTimeout);
    }

    public Task DeleteChatAsync(
        Guid chatId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            HttpMethod.Delete,
            $"assistant/chats/{chatId:D}",
            cancellationToken,
            DefaultRequestTimeout);
    }

    public Task<IReadOnlyList<AssistantInteractionDto>> GetChatMessagesAsync(
        Guid chatId,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        int normalizedTake = Math.Clamp(take, 1, 200);
        return SendAsync<IReadOnlyList<AssistantInteractionDto>>(
            HttpMethod.Get,
            $"assistant/chats/{chatId:D}/messages?take={normalizedTake}",
            cancellationToken: cancellationToken,
            timeout: DefaultRequestTimeout);
    }

    public Task<AssistantCommandResponse> SendChatMessageAsync(
        Guid chatId,
        AssistantCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<AssistantCommandResponse>(
            HttpMethod.Post,
            $"assistant/chats/{chatId:D}/messages",
            request,
            cancellationToken,
            MessageRequestTimeout);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        using HttpResponseMessage response = await SendWithAuthorizationAsync(
            method,
            relativePath,
            body,
            cancellationToken,
            timeout ?? DefaultRequestTimeout);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                await BuildErrorMessageAsync(response, cancellationToken));
        }

        T? result = await response.Content.ReadFromJsonAsync<T>(
            JsonSerializerOptions,
            cancellationToken);

        if (result == null)
        {
            throw new InvalidOperationException("The assistant API returned an empty response.");
        }

        return result;
    }

    private async Task SendAsync(
        HttpMethod method,
        string relativePath,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        using HttpResponseMessage response = await SendWithAuthorizationAsync(
            method,
            relativePath,
            null,
            cancellationToken,
            timeout ?? DefaultRequestTimeout);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                await BuildErrorMessageAsync(response, cancellationToken));
        }
    }

    private async Task<HttpResponseMessage> SendWithAuthorizationAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        AssistantClientSettings settings = settingsService.Load();
        ValidateSettings(settings);

        using CancellationTokenSource timeoutCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(timeout);

        HttpRequestMessage requestMessage = await CreateRequestMessageAsync(
            method,
            settings,
            relativePath,
            body,
            timeoutCancellationTokenSource.Token);

        HttpResponseMessage response = await httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutCancellationTokenSource.Token);

        if (response.StatusCode == HttpStatusCode.Unauthorized && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            await authenticationService.SignOutAsync();
            await navigationService.ShowLoginAsync();
        }

        return response;
    }

    private async Task<HttpRequestMessage> CreateRequestMessageAsync(
        HttpMethod method,
        AssistantClientSettings settings,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        HttpRequestMessage requestMessage = new(method, BuildUri(settings.ApiBaseUrl, relativePath));
        requestMessage.Headers.TryAddWithoutValidation("Accept", "application/json");

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            requestMessage.Headers.TryAddWithoutValidation("x-api-key", settings.ApiKey);
        }
        else
        {
            string? accessToken = await authenticationService.GetAccessTokenAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                await navigationService.ShowLoginAsync();
                throw new InvalidOperationException("Please sign in to NOAH to continue.");
            }

            requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body != null)
        {
            requestMessage.Content = JsonContent.Create(body, options: JsonSerializerOptions);
        }

        return requestMessage;
    }

    private static Uri BuildUri(string apiBaseUrl, string relativePath)
    {
        string normalizedBaseUrl = apiBaseUrl.Trim().TrimEnd('/');
        string normalizedRelativePath = relativePath.Trim().TrimStart('/');
        return new Uri($"{normalizedBaseUrl}/{normalizedRelativePath}", UriKind.Absolute);
    }

    private static void ValidateSettings(AssistantClientSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
        {
            throw new InvalidOperationException("NOAH is not connected yet.");
        }

        if (!Uri.TryCreate(settings.ApiBaseUrl.Trim(), UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("The NOAH connection is invalid.");
        }
    }

    private static async Task<string> BuildErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string fallbackMessage = $"The assistant API returned {(int)response.StatusCode} {response.StatusCode}.";
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return response.StatusCode == HttpStatusCode.Unauthorized
                ? "Your NOAH session expired. Please sign in again."
                : fallbackMessage;
        }

        try
        {
            ProblemDetailsLite? problem = JsonSerializer.Deserialize<ProblemDetailsLite>(
                responseBody,
                JsonSerializerOptions);

            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }

            if (!string.IsNullOrWhiteSpace(problem?.Title))
            {
                return problem.Title;
            }
        }
        catch (JsonException)
        {
        }

        return response.StatusCode == HttpStatusCode.Unauthorized
            ? "Your NOAH session expired. Please sign in again."
            : responseBody.Trim();
    }

    private sealed record ProblemDetailsLite(
        string? Type,
        string? Title,
        int? Status,
        string? Detail,
        string? TraceId);
}
