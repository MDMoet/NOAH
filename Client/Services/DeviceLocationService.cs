using Client.Models;
using Microsoft.Maui.Devices.Sensors;

namespace Client.Services;

/// <summary>
/// Reads the user's current device location after requesting permission.
/// </summary>
public interface IUserLocationService
{
    /// <summary>
    /// Attempts to get the current device location.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The resolved location or a failure description.</returns>
    Task<CurrentLocationResult> TryGetCurrentLocationAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Wraps the result of a current-location request.
/// </summary>
public sealed record CurrentLocationResult(
    bool IsAvailable,
    bool PermissionDenied,
    GeoCoordinateDto? Coordinate,
    string? ErrorMessage);

/// <summary>
/// Uses MAUI Essentials to request permission and read the current location.
/// </summary>
public sealed class DeviceLocationService : IUserLocationService
{
    private const double MaximumUsefulAccuracyMeters = 1000;
    private const string LocationPermissionConfirmedPreferenceKey = "location_permission_confirmed";
    private static readonly TimeSpan MaximumCachedLocationAge = TimeSpan.FromMinutes(10);

    public async Task<CurrentLocationResult> TryGetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            bool permissionWasPreviouslyGranted = Preferences.Default.Get(LocationPermissionConfirmedPreferenceKey, false);
            PermissionStatus permissionStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (permissionStatus != PermissionStatus.Granted && !permissionWasPreviouslyGranted)
            {
                permissionStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (permissionStatus == PermissionStatus.Granted)
            {
                Preferences.Default.Set(LocationPermissionConfirmedPreferenceKey, true);
            }

            if (permissionStatus != PermissionStatus.Granted && !permissionWasPreviouslyGranted)
            {
                return new CurrentLocationResult(
                    false,
                    true,
                    null,
                    "Location access was denied.");
            }

            Location? location = await Geolocation.Default.GetLastKnownLocationAsync();

            if (!IsAccurateEnough(location) || IsStale(location))
            {
                location = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(15)),
                    cancellationToken);
            }

            if (location == null)
            {
                return new CurrentLocationResult(
                    false,
                    false,
                    null,
                    "NOAH could not determine the current location.");
            }

            if (!IsAccurateEnough(location))
            {
                return new CurrentLocationResult(
                    false,
                    false,
                    null,
                    $"NOAH got a current location, but its accuracy was about {location.Accuracy:0} meters. Please try again when Windows can provide a more accurate location.");
            }

            return new CurrentLocationResult(
                true,
                false,
                new GeoCoordinateDto(location.Latitude, location.Longitude, location.Accuracy),
                null);
        }
        catch (FeatureNotSupportedException)
        {
            return new CurrentLocationResult(false, false, null, "Location is not supported on this device.");
        }
        catch (PermissionException)
        {
            return new CurrentLocationResult(false, true, null, "Location access was denied.");
        }
        catch (Exception exception)
        {
            return new CurrentLocationResult(false, false, null, $"Location failed: {exception.Message}");
        }
    }

    private static bool IsAccurateEnough(Location? location)
    {
        return location != null &&
               (!location.Accuracy.HasValue || location.Accuracy.Value <= MaximumUsefulAccuracyMeters);
    }

    private static bool IsStale(Location? location)
    {
        return location == null ||
               DateTimeOffset.UtcNow - location.Timestamp.ToUniversalTime() > MaximumCachedLocationAge;
    }
}