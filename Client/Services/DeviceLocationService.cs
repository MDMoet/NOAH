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
    public async Task<CurrentLocationResult> TryGetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            PermissionStatus permissionStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (permissionStatus != PermissionStatus.Granted)
            {
                return new CurrentLocationResult(
                    false,
                    true,
                    null,
                    "Location access was denied.");
            }

            Location? location = await Geolocation.Default.GetLastKnownLocationAsync();

            if (location == null)
            {
                location = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)),
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
}
