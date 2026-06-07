using Api.Helpers;
using Api.Interfaces;
using Api.Interfaces.Providers;
using Microsoft.EntityFrameworkCore;
using NOAH.Contracts.Common;
using NOAH.Contracts.Locations;
using NOAH.Domain.Entities;
using NOAH.Domain.ValueObjects;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

/// <summary>
/// Handles saved-location persistence and delegates live location lookups to configured providers.
/// </summary>
public sealed class LocationsService(
    NoahDbContext noahDbContext,
    IPlacesProvider placesProvider,
    IGeocodingProvider geocodingProvider)
    : ILocationsService
{
    /// <summary>
    /// Saves the user's current location with a friendly name.
    /// </summary>
    /// <param name="request">The location details used to create the saved location.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The created saved location.</returns>
    public async Task<SavedLocationDto> SaveCurrentLocationAsync(
        SaveCurrentLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset currentDateTimeUtc = DateTimeOffset.UtcNow;

        SavedLocation savedLocation = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Coordinate = MapToValueObject(request.Coordinate),
            Address = NormalizeOptionalText(request.Address),
            CreatedFromCurrentLocation = true,
            CreatedAtUtc = currentDateTimeUtc,
            UpdatedAtUtc = null
        };

        noahDbContext.SavedLocations.Add(savedLocation);

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(savedLocation);
    }

    /// <summary>
    /// Gets all saved locations.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A read-only list of saved locations.</returns>
    public async Task<IReadOnlyList<SavedLocationDto>> GetSavedLocationsAsync(
        CancellationToken cancellationToken = default)
    {
        List<SavedLocationDto> savedLocations = await noahDbContext.SavedLocations
            .AsNoTracking()
            .OrderBy(savedLocation => savedLocation.Name)
            .ThenByDescending(savedLocation => savedLocation.CreatedAtUtc)
            .Select(savedLocation => new SavedLocationDto(
                savedLocation.Id,
                savedLocation.Name,
                new GeoCoordinateDto(
                    savedLocation.Coordinate.Latitude,
                    savedLocation.Coordinate.Longitude,
                    savedLocation.Coordinate.AccuracyMeters),
                savedLocation.Address,
                savedLocation.CreatedFromCurrentLocation,
                savedLocation.CreatedAtUtc,
                savedLocation.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return savedLocations;
    }

    /// <summary>
    /// Gets a saved location by id.
    /// </summary>
    /// <param name="savedLocationId">The unique identifier of the saved location.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The saved location when found; otherwise, null.</returns>
    public async Task<SavedLocationDto?> GetSavedLocationByIdAsync(
        Guid savedLocationId,
        CancellationToken cancellationToken = default)
    {
        SavedLocationDto? savedLocation = await noahDbContext.SavedLocations
            .AsNoTracking()
            .Where(savedLocation => savedLocation.Id == savedLocationId)
            .Select(savedLocation => new SavedLocationDto(
                savedLocation.Id,
                savedLocation.Name,
                new GeoCoordinateDto(
                    savedLocation.Coordinate.Latitude,
                    savedLocation.Coordinate.Longitude,
                    savedLocation.Coordinate.AccuracyMeters),
                savedLocation.Address,
                savedLocation.CreatedFromCurrentLocation,
                savedLocation.CreatedAtUtc,
                savedLocation.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return savedLocation;
    }

    /// <summary>
    /// Fully updates an existing saved location.
    /// </summary>
    /// <param name="savedLocationId">The unique identifier of the saved location to update.</param>
    /// <param name="request">The replacement saved-location details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated saved location when found; otherwise, null.</returns>
    public async Task<SavedLocationDto?> UpdateSavedLocationAsync(
        Guid savedLocationId,
        UpdateSavedLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        SavedLocation? savedLocation = await noahDbContext.SavedLocations
            .FirstOrDefaultAsync(savedLocation => savedLocation.Id == savedLocationId, cancellationToken);

        if (savedLocation == null)
        {
            return null;
        }

        savedLocation.Name = request.Name.Trim();
        savedLocation.Coordinate = MapToValueObject(request.Coordinate);
        savedLocation.Address = NormalizeOptionalText(request.Address);
        savedLocation.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(savedLocation);
    }

    /// <summary>
    /// Deletes an existing saved location.
    /// </summary>
    /// <param name="savedLocationId">The unique identifier of the saved location to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True when the saved location was deleted; otherwise, false.</returns>
    public async Task<bool> DeleteSavedLocationAsync(
        Guid savedLocationId,
        CancellationToken cancellationToken = default)
    {
        SavedLocation? savedLocation = await noahDbContext.SavedLocations
            .FirstOrDefaultAsync(savedLocation => savedLocation.Id == savedLocationId, cancellationToken);

        if (savedLocation == null)
        {
            return false;
        }

        noahDbContext.SavedLocations.Remove(savedLocation);

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Calculates the distance between two coordinates.
    /// </summary>
    /// <param name="request">The distance calculation request.</param>
    /// <returns>The calculated distance in kilometers.</returns>
    public DistanceResponse CalculateDistance(DistanceRequest request)
    {
        double distanceKilometers = LocationDistanceCalculator.CalculateKilometers(request.From, request.To);

        return new DistanceResponse(distanceKilometers);
    }

    /// <summary>
    /// Finds live nearby places using the configured places provider.
    /// </summary>
    /// <param name="request">The nearby places search details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The nearby places matching the request.</returns>
    public async Task<NearbyPlacesResponse> GetNearbyPlacesAsync(
        NearbyPlacesRequest request,
        CancellationToken cancellationToken = default)
    {
        return await placesProvider.GetNearbyPlacesAsync(request, cancellationToken);
    }

    /// <summary>
    /// Searches for coordinates by a human-readable location query.
    /// </summary>
    /// <param name="request">The geocoding search details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The geocoding results returned by the configured provider.</returns>
    public async Task<GeocodeResponse> GeocodeAsync(
        GeocodeRequest request,
        CancellationToken cancellationToken = default)
    {
        return await geocodingProvider.GeocodeAsync(request, cancellationToken);
    }

    /// <summary>
    /// Finds a human-readable location for a coordinate.
    /// </summary>
    /// <param name="request">The reverse geocoding details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The reverse geocoded location when found; otherwise, null.</returns>
    public async Task<ReverseGeocodeResponse?> ReverseGeocodeAsync(
        ReverseGeocodeRequest request,
        CancellationToken cancellationToken = default)
    {
        return await geocodingProvider.ReverseGeocodeAsync(request, cancellationToken);
    }

    /// <summary>
    /// Trims optional text and stores blank values as null.
    /// </summary>
    /// <param name="value">The text value to normalize.</param>
    /// <returns>The trimmed value, or null when the value is blank.</returns>
    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    /// <summary>
    /// Maps a coordinate contract model to the domain value object.
    /// </summary>
    /// <param name="coordinate">The coordinate DTO to map.</param>
    /// <returns>The domain coordinate value object.</returns>
    private static GeoCoordinate MapToValueObject(GeoCoordinateDto coordinate)
    {
        return new GeoCoordinate
        {
            Latitude = coordinate.Latitude,
            Longitude = coordinate.Longitude,
            AccuracyMeters = coordinate.AccuracyMeters
        };
    }

    /// <summary>
    /// Maps a saved location entity to its contract model.
    /// </summary>
    /// <param name="savedLocation">The saved location entity to map.</param>
    /// <returns>The saved location DTO.</returns>
    private static SavedLocationDto MapToDto(SavedLocation savedLocation)
    {
        return new SavedLocationDto(
            savedLocation.Id,
            savedLocation.Name,
            MapToDto(savedLocation.Coordinate),
            savedLocation.Address,
            savedLocation.CreatedFromCurrentLocation,
            savedLocation.CreatedAtUtc,
            savedLocation.UpdatedAtUtc);
    }

    /// <summary>
    /// Maps a domain coordinate value object to its contract model.
    /// </summary>
    /// <param name="coordinate">The coordinate value object to map.</param>
    /// <returns>The coordinate DTO.</returns>
    private static GeoCoordinateDto MapToDto(GeoCoordinate coordinate)
    {
        return new GeoCoordinateDto(
            coordinate.Latitude,
            coordinate.Longitude,
            coordinate.AccuracyMeters);
    }

}
