using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client.Models;

namespace Client.Services;

public sealed class NoahApiClient(
    HttpClient httpClient,
    AssistantApiSettingsService settingsService,
    NoahAuthenticationService authenticationService,
    AppNavigationService navigationService)
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Get, relativePath, cancellationToken: cancellationToken);

    public Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest request,
        CancellationToken cancellationToken = default)
        => SendAsync<TResponse>(HttpMethod.Post, relativePath, request, cancellationToken);

    public Task<TResponse> PutAsync<TRequest, TResponse>(
        string relativePath,
        TRequest request,
        CancellationToken cancellationToken = default)
        => SendAsync<TResponse>(HttpMethod.Put, relativePath, request, cancellationToken);

    public Task<TResponse> PatchAsync<TRequest, TResponse>(
        string relativePath,
        TRequest request,
        CancellationToken cancellationToken = default)
        => SendAsync<TResponse>(HttpMethod.Patch, relativePath, request, cancellationToken);

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
        => SendWithoutBodyAsync(HttpMethod.Delete, relativePath, cancellationToken);

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendWithAuthorizationAsync(
            method,
            relativePath,
            body,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await BuildErrorMessageAsync(response, cancellationToken));
        }

        T? result = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions, cancellationToken);

        if (result == null)
        {
            throw new InvalidOperationException("The NOAH API returned an empty response.");
        }

        return result;
    }

    private async Task SendWithoutBodyAsync(
        HttpMethod method,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendWithAuthorizationAsync(
            method,
            relativePath,
            null,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await BuildErrorMessageAsync(response, cancellationToken));
        }
    }

    private static Uri BuildUri(string apiBaseUrl, string relativePath)
    {
        string normalizedBaseUrl = apiBaseUrl.Trim().TrimEnd('/');
        string normalizedRelativePath = relativePath.Trim().TrimStart('/');
        return new Uri($"{normalizedBaseUrl}/{normalizedRelativePath}", UriKind.Absolute);
    }

    private async Task<HttpResponseMessage> SendWithAuthorizationAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        AssistantClientSettings settings = settingsService.Load();
        ValidateSettings(settings);

        HttpRequestMessage requestMessage = await CreateRequestMessageAsync(
            method,
            settings,
            relativePath,
            body,
            cancellationToken);

        HttpResponseMessage response = await httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

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
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(body))
        {
            return response.StatusCode == HttpStatusCode.Unauthorized
                ? "Your NOAH session expired. Please sign in again."
                : $"The NOAH API returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        try
        {
            ProblemDetailsLite? problem = JsonSerializer.Deserialize<ProblemDetailsLite>(body, JsonSerializerOptions);

            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail!;
            }

            if (!string.IsNullOrWhiteSpace(problem?.Title))
            {
                return problem.Title!;
            }
        }
        catch (JsonException)
        {
        }

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => "The requested NOAH resource was not found.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Your NOAH session expired. Please sign in again.",
            _ => body.Trim()
        };
    }

    private sealed record ProblemDetailsLite(string? Title, string? Detail);
}
