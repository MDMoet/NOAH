using NOAH.Contracts.Common;

namespace Api.Helpers;

/// <summary>
/// Calculates distances between geographic coordinates.
/// </summary>
public static class LocationDistanceCalculator
{
    private const double EarthRadiusKilometers = 6371.0088;

    /// <summary>
    /// Calculates the great-circle distance between two coordinates.
    /// </summary>
    /// <param name="from">The starting coordinate.</param>
    /// <param name="to">The destination coordinate.</param>
    /// <returns>The distance in kilometers.</returns>
    public static double CalculateKilometers(GeoCoordinateDto from, GeoCoordinateDto to)
    {
        // Haversine gives stable short-distance results for nearby POI searches.
        double latitudeRadiansFrom = ConvertDegreesToRadians(from.Latitude);
        double latitudeRadiansTo = ConvertDegreesToRadians(to.Latitude);
        double latitudeDifferenceRadians = ConvertDegreesToRadians(to.Latitude - from.Latitude);
        double longitudeDifferenceRadians = ConvertDegreesToRadians(to.Longitude - from.Longitude);

        double haversine =
            Math.Sin(latitudeDifferenceRadians / 2) * Math.Sin(latitudeDifferenceRadians / 2) +
            Math.Cos(latitudeRadiansFrom) * Math.Cos(latitudeRadiansTo) *
            Math.Sin(longitudeDifferenceRadians / 2) * Math.Sin(longitudeDifferenceRadians / 2);

        double centralAngle = 2 * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine));

        return EarthRadiusKilometers * centralAngle;
    }

    private static double ConvertDegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }
}
