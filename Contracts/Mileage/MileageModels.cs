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

public sealed record MileageSummaryDto(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int EntryCount,
    decimal? FirstOdometerReadingKm,
    decimal? LastOdometerReadingKm,
    decimal TotalTripDistanceKm,
    decimal? EstimatedDistanceKm,
    decimal? AverageTripDistanceKm);
