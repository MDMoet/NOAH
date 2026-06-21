namespace NOAH.Contracts.Auth;

/// <summary>
/// Requests a temporary NOAH access token.
/// </summary>
/// <param name="Username">The configured NOAH username.</param>
/// <param name="Password">The configured NOAH password.</param>
public sealed record LoginRequest(
    string Username,
    string Password);

/// <summary>
/// Describes the authenticated NOAH user.
/// </summary>
/// <param name="Username">The unique NOAH username.</param>
/// <param name="DisplayName">The friendly display name shown by clients.</param>
public sealed record AuthenticatedUserDto(
    string Username,
    string DisplayName);

/// <summary>
/// Returns the issued bearer token and the user it belongs to.
/// </summary>
/// <param name="AccessToken">The signed bearer token.</param>
/// <param name="TokenType">The HTTP authorization scheme, usually Bearer.</param>
/// <param name="ExpiresAtUtc">When the token expires in UTC.</param>
/// <param name="User">The authenticated NOAH user.</param>
public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    AuthenticatedUserDto User);
