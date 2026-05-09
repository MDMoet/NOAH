using NOAH.Contracts.Common;

namespace NOAH.Contracts.Locations;

public sealed record SavedLocationDto(
    Guid Id,
    string Name,
    GeoCoordinateDto Coordinate,
    string? Address,
    bool CreatedFromCurrentLocation,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record SaveCurrentLocationRequest(
    string Name,
    GeoCoordinateDto Coordinate,
    string? Address);

public sealed record UpdateSavedLocationRequest(
    string Name,
    GeoCoordinateDto Coordinate,
    string? Address);

public sealed record DistanceRequest(
    GeoCoordinateDto From,
    GeoCoordinateDto To);

public sealed record DistanceResponse(
    double DistanceKilometers);

public sealed record NearbyPlacesRequest(
    GeoCoordinateDto Origin,
    string Query,
    double RadiusKilometers);

public sealed record NearbyPlaceDto(
    string Name,
    string? Category,
    GeoCoordinateDto Coordinate,
    string? Address,
    double DistanceKilometers);

public sealed record NearbyPlacesResponse(
    IReadOnlyList<NearbyPlaceDto> Places);
