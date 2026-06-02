using System.Security.Cryptography;
using System.Text;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Middleware;

public sealed class ApiKeyMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        if (ShouldSkipApiKeyCheck(httpContext))
        {
            await next(httpContext);
            return;
        }

        string? configuredApiKey = configuration["Authentication:ApiKey"];

        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsync("Unauthorized.");
            return;
        }

        if (!httpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedApiKeyValues))
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await httpContext.Response.WriteAsync("Unauthorized.");
            return;
        }

        string? providedApiKey = providedApiKeyValues.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(providedApiKey) || !ApiKeysAreEqual(providedApiKey, configuredApiKey))
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await httpContext.Response.WriteAsync("Unauthorized.");
            return;
        }

        await next(httpContext);
    }

    private bool ShouldSkipApiKeyCheck(HttpContext httpContext)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
        {
            return true;
        }

        if (environment.IsDevelopment() &&
            httpContext.Request.Path.StartsWithSegments("/swagger"))
        {
            return true;
        }

        return false;
    }

    private static bool ApiKeysAreEqual(string providedApiKey, string configuredApiKey)
    {
        byte[] providedApiKeyBytes = Encoding.UTF8.GetBytes(providedApiKey);
        byte[] configuredApiKeyBytes = Encoding.UTF8.GetBytes(configuredApiKey);

        return providedApiKeyBytes.Length == configuredApiKeyBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedApiKeyBytes, configuredApiKeyBytes);
    }
}