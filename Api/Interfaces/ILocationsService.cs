using NOAH.Contracts.Locations;

namespace Api.Interfaces;

/// <summary>
/// Coordinates saved-location persistence and provider-backed location operations.
/// </summary>
public interface ILocationsService
{
    /// <summary>
    /// Saves the user's current location with a friendly name.
    /// </summary>
    /// <param name="request">The location details used to create the saved location.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The created saved location.</returns>
    Task<SavedLocationDto> SaveCurrentLocationAsync(
        SaveCurrentLocationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all saved locations that are currently available.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A read-only list of saved locations.</returns>
    Task<IReadOnlyList<SavedLocationDto>> GetSavedLocationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a saved location by its unique identifier.
    /// </summary>
    /// <param name="savedLocationId">The unique identifier of the saved location.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The saved location when found; otherwise, null.</returns>
    Task<SavedLocationDto?> GetSavedLocationByIdAsync(
        Guid savedLocationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully updates an existing saved location by replacing its editable fields.
    /// </summary>
    /// <param name="savedLocationId">The unique identifier of the saved location to update.</param>
    /// <param name="request">The new saved location details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated saved location when found; otherwise, null.</returns>
    Task<SavedLocationDto?> UpdateSavedLocationAsync(
        Guid savedLocationId,
        UpdateSavedLocationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an existing saved location by its unique identifier.
    /// </summary>
    /// <param name="savedLocationId">The unique identifier of the saved location to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True when the saved location was deleted; otherwise, false.</returns>
    Task<bool> DeleteSavedLocationAsync(Guid savedLocationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the distance between two coordinates.
    /// </summary>
    /// <param name="request">The two coordinates used to calculate distance.</param>
    /// <returns>The distance in kilometers.</returns>
    DistanceResponse CalculateDistance(DistanceRequest request);

    /// <summary>
    /// Finds real-world places near an origin that match the provided query.
    /// </summary>
    /// <param name="request">The nearby places search details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Nearby places matching the search criteria.</returns>
    Task<NearbyPlacesResponse> GetNearbyPlacesAsync(
        NearbyPlacesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for locations by a human-readable query.
    /// </summary>
    /// <param name="request">The geocoding search details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Locations matching the search criteria.</returns>
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
