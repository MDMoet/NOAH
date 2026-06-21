using NOAH.Contracts.Enums;

namespace Client.Models;

/// <summary>
/// Represents a single odometer reading / mileage log entry.
/// </summary>
public class MileageEntry
{
    public Guid Id { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    public double Odometer { get; set; }

    public double TripKm { get; set; }

    public MileageEntrySourceDto Source { get; set; } = MileageEntrySourceDto.Manual;

    public string? SourceImagePath { get; set; }

    public string? RecognizedText { get; set; }

    public string? CorrectedText { get; set; }

    public double? LocationLatitude { get; set; }

    public double? LocationLongitude { get; set; }

    public double? LocationAccuracyMeters { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public DateTime Timestamp => RecordedAtUtc.LocalDateTime;

    public string SourceDisplay => Source switch
    {
        MileageEntrySourceDto.Manual => "Manual",
        MileageEntrySourceDto.PhotoOcr => "Photo OCR",
        MileageEntrySourceDto.VoiceCommand => "Voice",
        _ => Source.ToString()
    };

    public string DateDisplay
    {
        get
        {
            DateTime timestamp = Timestamp;
            string day = timestamp.Date == DateTime.Today
                ? "Today"
                : timestamp.ToString("d MMMM");
            string time = timestamp.ToString("H:mm");
            return $"{day}\n{time}";
        }
    }

    public string OdometerDisplay => $"{Odometer:N0} km";

    public string TripDisplay => $"{TripKm:N1} km";
}
