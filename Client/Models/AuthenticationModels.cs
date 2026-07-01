namespace Client.Models;

/// <summary>
/// Stores the static connection settings used by the MAUI client.
/// </summary>
public sealed record AssistantClientSettings(
    string ApiBaseUrl,
    string ApiKey,
    Guid? LastSelectedChatId,
    bool ShareLocationAutomatically);

/// <summary>
/// Requests a temporary NOAH access token.
/// </summary>
public sealed record LoginRequest(
    string Username,
    string Password);

/// <summary>
/// Describes the authenticated NOAH user returned by the API.
/// </summary>
public sealed record AuthenticatedUserDto(
    string Username,
    string DisplayName);

/// <summary>
/// Stores the issued bearer token used by the MAUI client.
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    AuthenticatedUserDto User);
