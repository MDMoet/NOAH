using NOAH.Contracts.Locations;

namespace Api.Interfaces.Providers;

/// <summary>
/// Provides live nearby place search from an external or self-hosted places source.
/// </summary>
public interface IPlacesProvider
{
    /// <summary>
    /// Finds real-world places near an origin that match the provided query.
    /// </summary>
    /// <param name="request">The nearby places search details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The nearby places matching the request.</returns>
    Task<NearbyPlacesResponse> GetNearbyPlacesAsync(
        NearbyPlacesRequest request,
        CancellationToken cancellationToken = default);
}
