using NOAH.Domain.Common;
using NOAH.Domain.Enums;
using NOAH.Domain.ValueObjects;

namespace NOAH.Domain.Entities;

public sealed class MileageEntry : TrackedEntity
{
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public decimal OdometerReadingKm { get; set; }

    public decimal? TripDistanceKm { get; set; }

    public MileageEntrySource Source { get; set; } = MileageEntrySource.Manual;

    public string? SourceImagePath { get; set; }

    public string? RecognizedText { get; set; }

    public string? CorrectedText { get; set; }

    public bool IsTextCorrected { get; set; }

    public GeoCoordinate? Location { get; set; }

    public string? Notes { get; set; }
}
