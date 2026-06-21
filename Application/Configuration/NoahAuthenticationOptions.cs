namespace Application.Configuration;

/// <summary>
/// Binds the NOAH authentication settings.
/// </summary>
public sealed class NoahAuthenticationOptions
{
    /// <summary>
    /// The configuration section name used to bind authentication settings.
    /// </summary>
    public const string SectionName = "Authentication";

    /// <summary>
    /// Gets or sets the legacy API key accepted for trusted internal clients.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JWT settings used for temporary external access.
    /// </summary>
    public NoahJwtOptions Jwt { get; set; } = new();
}

/// <summary>
/// Describes how NOAH should issue JWT bearer tokens.
/// </summary>
public sealed class NoahJwtOptions
{
    /// <summary>
    /// Gets or sets the token issuer value.
    /// </summary>
    public string Issuer { get; set; } = "NOAH.Api";

    /// <summary>
    /// Gets or sets the token audience value.
    /// </summary>
    public string Audience { get; set; } = "NOAH.Client";

    /// <summary>
    /// Gets or sets the signing key used for bearer tokens.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of minutes a temporary access token stays valid.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 480;
}
