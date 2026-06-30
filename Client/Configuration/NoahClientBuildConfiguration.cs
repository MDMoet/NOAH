namespace Client.Configuration;

/// <summary>
/// Centralizes the build-time NOAH connection values used by the MAUI client.
/// </summary>
public static class NoahClientBuildConfiguration
{
    /// <summary>
    /// The default API base URL used by Android builds.
    /// </summary>
    public const string ApiBaseUrl = "https://100.74.230.23:1793/api";

    /// <summary>
    /// When this is filled in, the client skips the login screen and uses the trusted API-key path.
    /// Leave it empty for the demo login flow.
    /// </summary>
    public const string ApiKey = "/sbC@cxaF<V+wt+~1{U<%O,WOQ>9J~+lax#Y2a%Uaddw1?fJ";
}
