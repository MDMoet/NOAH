using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Application.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace NOAH.Api.Authentication;

/// <summary>
/// Authenticates trusted internal requests by comparing the configured API key.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<NoahAuthenticationOptions> authenticationOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string configuredApiKey = authenticationOptions.Value.ApiKey;

        if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedApiKeyValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string? providedApiKey = providedApiKeyValues.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(configuredApiKey) || string.IsNullOrWhiteSpace(providedApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("The provided API key is invalid."));
        }

        if (!ApiKeysAreEqual(providedApiKey, configuredApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("The provided API key is invalid."));
        }

        ClaimsIdentity identity = new(
        [
            new Claim(ClaimTypes.NameIdentifier, "internal-api-key"),
            new Claim(ClaimTypes.Name, "NOAH API key"),
            new Claim("noah_auth_method", "api-key")
        ], Scheme.Name);

        AuthenticationTicket ticket = new(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool ApiKeysAreEqual(string providedApiKey, string configuredApiKey)
    {
        byte[] providedApiKeyBytes = Encoding.UTF8.GetBytes(providedApiKey);
        byte[] configuredApiKeyBytes = Encoding.UTF8.GetBytes(configuredApiKey);

        return providedApiKeyBytes.Length == configuredApiKeyBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedApiKeyBytes, configuredApiKeyBytes);
    }
}
