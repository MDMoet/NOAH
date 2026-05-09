namespace NOAH.Contracts.Common;

public sealed record GeoCoordinateDto(
    double Latitude,
    double Longitude,
    double? AccuracyMeters = null);
