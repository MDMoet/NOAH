using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application.Configuration;
using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NOAH.Contracts.Auth;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

/// <summary>
/// Validates demo users and issues signed JWT access tokens for clients outside the trusted network.
/// </summary>
public sealed class DemoUserAuthenticationService(
    DemoAuthenticationDbContext demoAuthenticationDbContext,
    IOptions<NoahAuthenticationOptions> authenticationOptions)
    : IAuthenticationService
{
    private readonly NoahAuthenticationOptions options = authenticationOptions.Value;

    /// <summary>
    /// Validates the provided credentials and returns a signed bearer token.
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedUsername = request.Username?.Trim() ?? string.Empty;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DemoUserCredential? demoUser = await demoAuthenticationDbContext.DemoUsers
            .FirstOrDefaultAsync(
                candidate => candidate.IsEnabled && candidate.Username == normalizedUsername,
                cancellationToken);

        if (demoUser is null || !VerifyPassword(request.Password, demoUser))
        {
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        DemoUserCredential authenticatedUser = demoUser;

        if (string.IsNullOrWhiteSpace(options.Jwt.SigningKey))
        {
            throw new InvalidOperationException("JWT signing is not configured.");
        }

        DateTimeOffset expiresAtUtc = now.AddMinutes(Math.Clamp(options.Jwt.AccessTokenMinutes, 5, 10080));
        string resolvedUsername = authenticatedUser.Username.Trim();
        string displayName = string.IsNullOrWhiteSpace(authenticatedUser.DisplayName)
            ? resolvedUsername
            : authenticatedUser.DisplayName.Trim();

        authenticatedUser.LastSignedInAtUtc = now;
        authenticatedUser.UpdatedAtUtc = now;
        await demoAuthenticationDbContext.SaveChangesAsync(cancellationToken);

        SymmetricSecurityKey signingKey = new(Encoding.UTF8.GetBytes(options.Jwt.SigningKey));
        SigningCredentials signingCredentials = new(signingKey, SecurityAlgorithms.HmacSha256);
        JwtSecurityToken token = new(
            issuer: options.Jwt.Issuer,
            audience: options.Jwt.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, resolvedUsername),
                new Claim(JwtRegisteredClaimNames.UniqueName, resolvedUsername),
                new Claim(ClaimTypes.NameIdentifier, resolvedUsername),
                new Claim(ClaimTypes.Name, displayName),
                new Claim("noah_auth_method", "bearer"),
                new Claim("noah_database_scope", "demo")
            ],
            notBefore: now.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: signingCredentials);

        string accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return new LoginResponse(
            accessToken,
            "Bearer",
            expiresAtUtc,
            new AuthenticatedUserDto(resolvedUsername, displayName));
    }

    /// <summary>
    /// Maps the authenticated claims principal to the NOAH user contract.
    /// </summary>
    public AuthenticatedUserDto GetCurrentUser(ClaimsPrincipal principal)
    {
        string username = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                          principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                          principal.Identity?.Name ??
                          "unknown";

        string displayName = principal.FindFirstValue(ClaimTypes.Name) ??
                             principal.FindFirstValue(JwtRegisteredClaimNames.UniqueName) ??
                             username;

        return new AuthenticatedUserDto(username, displayName);
    }

    private static bool VerifyPassword(string? providedSecret, DemoUserCredential demoUser)
    {
        if (string.IsNullOrWhiteSpace(providedSecret) ||
            string.IsNullOrWhiteSpace(demoUser.PasswordSalt) ||
            string.IsNullOrWhiteSpace(demoUser.PasswordHash))
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(demoUser.PasswordSalt);
            byte[] expectedHash = Convert.FromBase64String(demoUser.PasswordHash);
            int iterations = Math.Clamp(demoUser.PasswordIterations, 10000, 600000);

            foreach (string passwordCandidate in GetPasswordCandidates(providedSecret.Trim()))
            {
                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    passwordCandidate,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length);

                if (CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                {
                    return true;
                }
            }

            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IEnumerable<string> GetPasswordCandidates(string providedSecret)
    {
        yield return providedSecret;

        string normalizedSmartQuotes = providedSecret
            .Replace('\u201D', '\u201C')
            .Replace('"', '\u201C');

        if (!string.Equals(normalizedSmartQuotes, providedSecret, StringComparison.Ordinal))
        {
            yield return normalizedSmartQuotes;
        }
    }
}
