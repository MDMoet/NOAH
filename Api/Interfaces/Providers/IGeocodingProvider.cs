using NOAH.Contracts.Locations;

namespace Api.Interfaces.Providers;

/// <summary>
/// Provides geocoding and reverse geocoding from an external or self-hosted source.
/// </summary>
public interface IGeocodingProvider
{
    /// <summary>
    /// Searches for locations by a human-readable query.
    /// </summary>
    /// <param name="request">The geocoding search details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The locations matching the query.</returns>
    Task<GeocodeResponse> GeocodeAsync(
        GeocodeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a human-readable location for a coordinate.
    /// </summary>
    /// <param name="request">The reverse geocoding details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The reverse geocoded location when found; otherwise, null.</returns>
    Task<ReverseGeocodeResponse?> ReverseGeocodeAsync(
        ReverseGeocodeRequest request,
        CancellationToken cancellationToken = default);
}
