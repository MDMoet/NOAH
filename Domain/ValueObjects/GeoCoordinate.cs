namespace NOAH.Domain.ValueObjects;

public sealed record GeoCoordinate
{
    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public double? AccuracyMeters { get; init; }
}
