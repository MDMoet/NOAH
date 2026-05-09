using NOAH.Contracts.Common;
using NOAH.Contracts.Enums;

namespace NOAH.Contracts.Mileage;

public sealed record MileageEntryDto(
    Guid Id,
    DateTimeOffset RecordedAtUtc,
    decimal OdometerReadingKm,
    decimal? TripDistanceKm,
    MileageEntrySourceDto Source,
    string? SourceImagePath,
    string? RecognizedText,
    string? CorrectedText,
    bool IsTextCorrected,
    GeoCoordinateDto? Location,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateMileageEntryRequest(
    DateTimeOffset RecordedAtUtc,
    decimal OdometerReadingKm,
    decimal? TripDistanceKm,
    MileageEntrySourceDto Source,
    string? SourceImagePath,
    string? RecognizedText,
    string? CorrectedText,
    GeoCoordinateDto? Location,
    string? Notes);

public sealed record UpdateMileageEntryRequest(
    DateTimeOffset RecordedAtUtc,
    decimal OdometerReadingKm,
    decimal? TripDistanceKm,
    string? CorrectedText,
    GeoCoordinateDto? Location,
    string? Notes);

public sealed record ProcessMileagePhotoRequest(
    string ImageContentBase64,
    string MimeType,
    DateTimeOffset CapturedAtUtc,
    GeoCoordinateDto? Location);

public sealed record ProcessMileagePhotoResponse(
    string RecognizedText,
    decimal? OdometerReadingKm,
    double? Confidence);
