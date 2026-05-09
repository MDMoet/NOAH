using NOAH.Domain.Common;
using NOAH.Domain.ValueObjects;

namespace NOAH.Domain.Entities;

public sealed class SavedLocation : TrackedEntity
{
    public string Name { get; set; } = string.Empty;

    public GeoCoordinate Coordinate { get; set; } = new();

    public string? Address { get; set; }

    public bool CreatedFromCurrentLocation { get; set; }
}
