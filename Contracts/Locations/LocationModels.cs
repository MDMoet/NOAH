using NOAH.Contracts.Common;

namespace NOAH.Contracts.Locations;

/// <summary>
/// Represents a location the user has explicitly saved in NOAH.
/// </summary>
/// <param name="Id">The unique identifier of the saved location.</param>
/// <param name="Name">The user-facing name for the location.</param>
/// <param name="Coordinate">The latitude and longitude of the saved location.</param>
/// <param name="Address">The optional human-readable address for the location.</param>
/// <param name="CreatedFromCurrentLocation">Whether the location was saved from the user's current position.</param>
/// <param name="CreatedAtUtc">The UTC timestamp when the location was created.</param>
/// <param name="UpdatedAtUtc">The UTC timestamp when the location was last updated.</param>
public sealed record SavedLocationDto(
    Guid Id,
    string Name,
    GeoCoordinateDto Coordinate,
    string? Address,
    bool CreatedFromCurrentLocation,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>
/// Request used to save the user's current location under a friendly name.
/// </summary>
/// <param name="Name">The user-facing name for the location.</param>
/// <param name="Coordinate">The latitude and longitude to save.</param>
/// <param name="Address">The optional human-readable address for the location.</param>
public sealed record SaveCurrentLocationRequest(
    string Name,
    GeoCoordinateDto Coordinate,
    string? Address);

/// <summary>
/// Request used to replace the editable fields of an existing saved location.
/// </summary>
/// <param name="Name">The updated user-facing name for the location.</param>
/// <param name="Coordinate">The updated latitude and longitude.</param>
/// <param name="Address">The updated optional human-readable address.</param>
public sealed record UpdateSavedLocationRequest(
    string Name,
    GeoCoordinateDto Coordinate,
    string? Address);

/// <summary>
/// Request used to calculate the distance between two coordinates.
/// </summary>
/// <param name="From">The starting coordinate.</param>
/// <param name="To">The destination coordinate.</param>
public sealed record DistanceRequest(
    GeoCoordinateDto From,
    GeoCoordinateDto To);

/// <summary>
/// Response containing the calculated distance between two coordinates.
/// </summary>
/// <param name="DistanceKilometers">The distance in kilometers.</param>
public sealed record DistanceResponse(
    double DistanceKilometers);

/// <summary>
/// Request used to find live nearby places around a coordinate.
/// </summary>
/// <param name="Origin">The coordinate to search around.</param>
/// <param name="Query">The place type or name to search for.</param>
/// <param name="RadiusKilometers">The maximum search radius in kilometers.</param>
public sealed record NearbyPlacesRequest(
    GeoCoordinateDto Origin,
    string Query,
    double RadiusKilometers);

/// <summary>
/// Represents a live nearby place returned by a places provider.
/// </summary>
/// <param name="Name">The display name of the place.</param>
/// <param name="Category">The optional category inferred from the provider metadata.</param>
/// <param name="Coordinate">The latitude and longitude of the place.</param>
/// <param name="Address">The optional human-readable address of the place.</param>
/// <param name="DistanceKilometers">The distance from the requested origin in kilometers.</param>
public sealed record NearbyPlaceDto(
    string Name,
    string? Category,
    GeoCoordinateDto Coordinate,
    string? Address,
    double DistanceKilometers);

/// <summary>
/// Response containing live nearby places.
/// </summary>
/// <param name="Places">The nearby places matching the request.</param>
public sealed record NearbyPlacesResponse(
    IReadOnlyList<NearbyPlaceDto> Places);

/// <summary>
/// Request used to convert a human-readable location query into coordinates.
/// </summary>
/// <param name="Query">The address, place, or location text to search for.</param>
public sealed record GeocodeRequest(
    string Query);

/// <summary>
/// Represents one geocoding search result.
/// </summary>
/// <param name="DisplayName">The provider's display name for the result.</param>
/// <param name="Category">The optional category inferred from the provider metadata.</param>
/// <param name="Coordinate">The latitude and longitude of the result.</param>
/// <param name="Address">The optional normalized address of the result.</param>
/// <param name="Importance">The optional provider-specific relevance score.</param>
public sealed record GeocodeResultDto(
    string DisplayName,
    string? Category,
    GeoCoordinateDto Coordinate,
    string? Address,
    double? Importance);

/// <summary>
/// Response containing geocoding search results.
/// </summary>
/// <param name="Results">The locations matching the geocoding query.</param>
public sealed record GeocodeResponse(
    IReadOnlyList<GeocodeResultDto> Results);

/// <summary>
/// Request used to convert coordinates into a human-readable location.
/// </summary>
/// <param name="Coordinate">The coordinate to reverse geocode.</param>
public sealed record ReverseGeocodeRequest(
    GeoCoordinateDto Coordinate);

/// <summary>
/// Response containing the human-readable location for a coordinate.
/// </summary>
/// <param name="DisplayName">The provider's display name for the coordinate.</param>
/// <param name="Category">The optional category inferred from the provider metadata.</param>
/// <param name="Coordinate">The coordinate returned by the provider.</param>
/// <param name="Address">The optional normalized address for the coordinate.</param>
public sealed record ReverseGeocodeResponse(
    string DisplayName,
    string? Category,
    GeoCoordinateDto Coordinate,
    string? Address);
