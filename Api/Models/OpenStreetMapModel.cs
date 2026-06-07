namespace Api.Options;

/// <summary>
/// Configuration root for self-hosted OpenStreetMap provider integrations.
/// </summary>
public sealed class OpenStreetMapModel
{
    /// <summary>
    /// The configuration section name used to bind OpenStreetMap provider settings.
    /// </summary>
    public const string SectionName = "OpenStreetMap";

    /// <summary>
    /// Gets or sets configuration for the Overpass places provider.
    /// </summary>
    public OverpassOptions Overpass { get; set; } = new();

    /// <summary>
    /// Gets or sets configuration for the Nominatim geocoding provider.
    /// </summary>
    public NominatimOptions Nominatim { get; set; } = new();
}

/// <summary>
/// Configuration used to call a self-hosted Overpass API instance.
/// </summary>
public sealed class OverpassOptions
{
    /// <summary>
    /// Gets or sets the absolute Overpass interpreter URL.
    /// </summary>
    public string? InterpreterUrl { get; set; }

    /// <summary>
    /// Gets or sets the maximum duration allowed for an Overpass request.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 25;

    /// <summary>
    /// Gets or sets the maximum number of places returned to API callers.
    /// </summary>
    public int MaxResults { get; set; } = 25;

    /// <summary>
    /// Gets or sets the user agent sent to the Overpass instance.
    /// </summary>
    public string UserAgent { get; set; } = "NOAH.Api/1.0";

    /// <summary>
    /// Gets or sets the optional authorization header name for a protected Overpass endpoint.
    /// </summary>
    public string? AuthorizationHeaderName { get; set; }

    /// <summary>
    /// Gets or sets the optional authorization header value for a protected Overpass endpoint.
    /// </summary>
    public string? AuthorizationHeaderValue { get; set; }
}

/// <summary>
/// Configuration used to call a self-hosted Nominatim instance.
/// </summary>
public sealed class NominatimOptions
{
    /// <summary>
    /// Gets or sets the absolute base URL of the Nominatim service.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the maximum duration allowed for a Nominatim request.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Gets or sets the maximum number of geocoding results returned to API callers.
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Gets or sets the user agent sent to the Nominatim instance.
    /// </summary>
    public string UserAgent { get; set; } = "NOAH.Api/1.0";

    /// <summary>
    /// Gets or sets the optional authorization header name for a protected Nominatim endpoint.
    /// </summary>
    public string? AuthorizationHeaderName { get; set; }

    /// <summary>
    /// Gets or sets the optional authorization header value for a protected Nominatim endpoint.
    /// </summary>
    public string? AuthorizationHeaderValue { get; set; }
}
