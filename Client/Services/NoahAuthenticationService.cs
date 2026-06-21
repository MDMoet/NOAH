using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client.Models;
using Microsoft.Maui.Storage;

namespace Client.Services;

/// <summary>
/// Manages the persisted bearer token used by the MAUI client.
/// </summary>
public sealed class NoahAuthenticationService(
    HttpClient httpClient,
    AssistantApiSettingsService settingsService)
{
    private const string SessionStorageKey = "noah_auth_session";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private LoginResponse? session;

    /// <summary>
    /// Returns true when this build uses the trusted API-key path instead of the demo login flow.
    /// </summary>
    public bool UsesTrustedApiKey()
    {
        return settingsService.HasTrustedApiKeyConfigured();
    }

    /// <summary>
    /// Returns true when the client can already access NOAH without an interactive sign-in.
    /// </summary>
    public async Task<bool> HasAccessAsync(CancellationToken cancellationToken = default)
    {
        return UsesTrustedApiKey() || await HasValidStoredSessionAsync(cancellationToken);
    }

    /// <summary>
    /// Signs in with the supplied credentials and persists the issued token securely on the device.
    /// </summary>
    public async Task<LoginResponse> SignInAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        AssistantClientSettings settings = settingsService.Load();

        using HttpRequestMessage requestMessage = new(
            HttpMethod.Post,
            BuildUri(settings.ApiBaseUrl, "auth/login"));
        requestMessage.Headers.TryAddWithoutValidation("Accept", "application/json");
        requestMessage.Content = JsonContent.Create(
            new LoginRequest(username.Trim(), password),
            options: JsonSerializerOptions);

        using HttpResponseMessage response = await httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string message = response.StatusCode == HttpStatusCode.Unauthorized
                ? "NOAH rejected those login credentials."
                : await BuildErrorMessageAsync(response, cancellationToken);

            throw new InvalidOperationException(message);
        }

        LoginResponse? loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(
            JsonSerializerOptions,
            cancellationToken);

        if (loginResponse == null || string.IsNullOrWhiteSpace(loginResponse.AccessToken))
        {
            throw new InvalidOperationException("NOAH did not return an access token.");
        }

        await SaveSessionAsync(loginResponse, cancellationToken);
        return loginResponse;
    }

    /// <summary>
    /// Gets a valid bearer token from memory or secure storage.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        LoginResponse? currentSession = await GetCurrentSessionAsync(cancellationToken);
        return currentSession?.AccessToken;
    }

    /// <summary>
    /// Returns the current persisted session when one is still valid.
    /// </summary>
    public async Task<LoginResponse?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        if (HasValidSession(session))
        {
            return session;
        }

        await sessionGate.WaitAsync(cancellationToken);

        try
        {
            if (HasValidSession(session))
            {
                return session;
            }

            string? serializedSession = await ReadSecureValueAsync(SessionStorageKey);

            if (string.IsNullOrWhiteSpace(serializedSession))
            {
                session = null;
                return null;
            }

            LoginResponse? storedSession = JsonSerializer.Deserialize<LoginResponse>(serializedSession, JsonSerializerOptions);

            if (!HasValidSession(storedSession))
            {
                await RemoveSecureValueAsync(SessionStorageKey);
                session = null;
                return null;
            }

            session = storedSession;
            return session;
        }
        finally
        {
            sessionGate.Release();
        }
    }

    /// <summary>
    /// Returns true when a valid persisted session already exists.
    /// </summary>
    public async Task<bool> HasValidStoredSessionAsync(CancellationToken cancellationToken = default)
    {
        return HasValidSession(await GetCurrentSessionAsync(cancellationToken));
    }

    /// <summary>
    /// Clears the persisted session so the user has to sign in again.
    /// </summary>
    public async Task SignOutAsync()
    {
        session = null;
        await RemoveSecureValueAsync(SessionStorageKey);
    }

    /// <summary>
    /// Clears only the in-memory cache. The stored session remains available until sign-out.
    /// </summary>
    public void InvalidateSession()
    {
        session = null;
    }

    private async Task SaveSessionAsync(LoginResponse loginResponse, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string serializedSession = JsonSerializer.Serialize(loginResponse, JsonSerializerOptions);
        await WriteSecureValueAsync(SessionStorageKey, serializedSession);
        session = loginResponse;
    }

    private static Uri BuildUri(string apiBaseUrl, string relativePath)
    {
        string normalizedBaseUrl = apiBaseUrl.Trim().TrimEnd('/');
        string normalizedRelativePath = relativePath.Trim().TrimStart('/');
        return new Uri($"{normalizedBaseUrl}/{normalizedRelativePath}", UriKind.Absolute);
    }

    private static bool HasValidSession(LoginResponse? currentSession)
    {
        return currentSession != null &&
               !string.IsNullOrWhiteSpace(currentSession.AccessToken) &&
               currentSession.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1);
    }

    private static async Task<string?> ReadSecureValueAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("NOAH could not read secure storage on this device.", exception);
        }
    }

    private static Task WriteSecureValueAsync(string key, string value)
    {
        try
        {
            return SecureStorage.Default.SetAsync(key, value);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("NOAH could not write secure storage on this device.", exception);
        }
    }

    private static Task RemoveSecureValueAsync(string key)
    {
        try
        {
            SecureStorage.Default.Remove(key);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("NOAH could not clear secure storage on this device.", exception);
        }
    }

    private static async Task<string> BuildErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(body))
        {
            return $"The NOAH auth endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        return body.Trim();
    }
}
