using System.Security.Claims;

namespace Api.Services;

/// <summary>
/// Chooses the database connection string for the current request.
/// </summary>
public interface IRequestConnectionStringResolver
{
    /// <summary>
    /// Resolves the primary NOAH connection string for the current request scope.
    /// </summary>
    string ResolveNoahConnectionString();

    /// <summary>
    /// Resolves the demo authentication database connection string.
    /// </summary>
    string ResolveDemoAuthenticationConnectionString();
}

/// <summary>
/// Sends trusted API-key requests to the normal database and bearer requests to the demo database.
/// </summary>
public sealed class RequestConnectionStringResolver(
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor)
    : IRequestConnectionStringResolver
{
    public string ResolveNoahConnectionString()
    {
        string? authMethod = httpContextAccessor.HttpContext?.User.FindFirstValue("noah_auth_method");
        string connectionStringName = string.Equals(authMethod, "bearer", StringComparison.OrdinalIgnoreCase)
            ? "DemoConnection"
            : "DefaultConnection";

        return ResolveRequiredConnectionString(connectionStringName, allowFallbackToDefault: true);
    }

    public string ResolveDemoAuthenticationConnectionString()
    {
        return ResolveRequiredConnectionString("DemoConnection", allowFallbackToDefault: false);
    }

    private string ResolveRequiredConnectionString(string connectionStringName, bool allowFallbackToDefault)
    {
        string? connectionString = configuration.GetConnectionString(connectionStringName);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        if (allowFallbackToDefault)
        {
            string? defaultConnection = configuration.GetConnectionString("DefaultConnection");

            if (!string.IsNullOrWhiteSpace(defaultConnection))
            {
                return defaultConnection;
            }
        }

        throw new InvalidOperationException(
            $"The connection string '{connectionStringName}' is not configured.");
    }
}
