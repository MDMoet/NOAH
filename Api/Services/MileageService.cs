using Api.Interfaces;
using Microsoft.EntityFrameworkCore;
using NOAH.Contracts.Common;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Mileage;
using NOAH.Domain.Entities;
using NOAH.Domain.Enums;
using NOAH.Domain.ValueObjects;
using NOAH.Infrastructure.Persistence;

namespace Api.Services;

/// <summary>
/// Handles mileage-entry persistence and history summaries.
/// </summary>
public sealed class MileageService(NoahDbContext noahDbContext)
    : IMileageService
{
    /// <summary>
    /// Creates a new mileage entry.
    /// </summary>
    /// <param name="request">The mileage details used to create the entry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The created mileage entry.</returns>
    public async Task<MileageEntryDto> CreateMileageEntryAsync(
        CreateMileageEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset currentDateTimeUtc = DateTimeOffset.UtcNow;
        string? recognizedText = NormalizeOptionalText(request.RecognizedText);
        string? correctedText = NormalizeOptionalText(request.CorrectedText);

        MileageEntry mileageEntry = new()
        {
            Id = Guid.NewGuid(),
            RecordedAtUtc = request.RecordedAtUtc.ToUniversalTime(),
            OdometerReadingKm = request.OdometerReadingKm,
            TripDistanceKm = request.TripDistanceKm,
            Source = (MileageEntrySource)request.Source,
            SourceImagePath = NormalizeOptionalText(request.SourceImagePath),
            RecognizedText = recognizedText,
            CorrectedText = correctedText,
            IsTextCorrected = IsCorrectedText(recognizedText, correctedText),
            Location = MapToValueObject(request.Location),
            Notes = NormalizeOptionalText(request.Notes),
            CreatedAtUtc = currentDateTimeUtc,
            UpdatedAtUtc = null
        };

        noahDbContext.MileageEntries.Add(mileageEntry);

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(mileageEntry);
    }

    /// <summary>
    /// Gets mileage entries ordered from newest to oldest.
    /// </summary>
    /// <param name="fromUtc">The optional inclusive lower recorded-at boundary.</param>
    /// <param name="toUtc">The optional inclusive upper recorded-at boundary.</param>
    /// <param name="take">The maximum number of entries to return.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The matching mileage entries.</returns>
    public async Task<IReadOnlyList<MileageEntryDto>> GetMileageEntriesAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int take,
        CancellationToken cancellationToken = default)
    {
        IQueryable<MileageEntry> query = ApplyDateFilters(noahDbContext.MileageEntries.AsNoTracking(), fromUtc, toUtc);

        List<MileageEntry> mileageEntries = await query
            .OrderByDescending(mileageEntry => mileageEntry.RecordedAtUtc)
            .ThenByDescending(mileageEntry => mileageEntry.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        return mileageEntries
            .Select(MapToDto)
            .ToList();
    }

    /// <summary>
    /// Gets a mileage entry by its unique identifier.
    /// </summary>
    /// <param name="mileageEntryId">The unique identifier of the mileage entry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The mileage entry when found; otherwise, null.</returns>
    public async Task<MileageEntryDto?> GetMileageEntryByIdAsync(
        Guid mileageEntryId,
        CancellationToken cancellationToken = default)
    {
        MileageEntry? mileageEntry = await noahDbContext.MileageEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(mileageEntry => mileageEntry.Id == mileageEntryId, cancellationToken);

        return mileageEntry == null
            ? null
            : MapToDto(mileageEntry);
    }

    /// <summary>
    /// Gets the newest mileage entry.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The latest mileage entry when one exists; otherwise, null.</returns>
    public async Task<MileageEntryDto?> GetLatestMileageEntryAsync(CancellationToken cancellationToken = default)
    {
        MileageEntry? mileageEntry = await noahDbContext.MileageEntries
            .AsNoTracking()
            .OrderByDescending(entry => entry.RecordedAtUtc)
            .ThenByDescending(entry => entry.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return mileageEntry == null
            ? null
            : MapToDto(mileageEntry);
    }

    /// <summary>
    /// Gets aggregate mileage statistics for an optional recorded-at range.
    /// </summary>
    /// <param name="fromUtc">The optional inclusive lower recorded-at boundary.</param>
    /// <param name="toUtc">The optional inclusive upper recorded-at boundary.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The mileage summary for the requested range.</returns>
    public async Task<MileageSummaryDto> GetMileageSummaryAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default)
    {
        IQueryable<MileageEntry> query = ApplyDateFilters(noahDbContext.MileageEntries.AsNoTracking(), fromUtc, toUtc);

        List<MileageEntry> mileageEntries = await query
            .OrderBy(entry => entry.RecordedAtUtc)
            .ThenBy(entry => entry.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (mileageEntries.Count == 0)
        {
            return new MileageSummaryDto(
                fromUtc?.ToUniversalTime(),
                toUtc?.ToUniversalTime(),
                0,
                null,
                null,
                0,
                null,
                null);
        }

        List<decimal> tripDistances = mileageEntries
            .Where(entry => entry.TripDistanceKm.HasValue)
            .Select(entry => entry.TripDistanceKm!.Value)
            .ToList();

        decimal totalTripDistanceKm = tripDistances.Sum();
        MileageEntry firstEntry = mileageEntries[0];
        MileageEntry lastEntry = mileageEntries[^1];
        decimal estimatedDistanceKm = Math.Max(0, lastEntry.OdometerReadingKm - firstEntry.OdometerReadingKm);
        decimal? averageTripDistanceKm = tripDistances.Count == 0
            ? null
            : totalTripDistanceKm / tripDistances.Count;

        return new MileageSummaryDto(
            fromUtc?.ToUniversalTime(),
            toUtc?.ToUniversalTime(),
            mileageEntries.Count,
            firstEntry.OdometerReadingKm,
            lastEntry.OdometerReadingKm,
            totalTripDistanceKm,
            mileageEntries.Count > 1 ? estimatedDistanceKm : null,
            averageTripDistanceKm);
    }

    /// <summary>
    /// Fully updates an existing mileage entry by replacing its editable fields.
    /// </summary>
    /// <param name="mileageEntryId">The unique identifier of the mileage entry to update.</param>
    /// <param name="request">The replacement mileage details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated mileage entry when found; otherwise, null.</returns>
    public async Task<MileageEntryDto?> UpdateMileageEntryAsync(
        Guid mileageEntryId,
        UpdateMileageEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        MileageEntry? mileageEntry = await noahDbContext.MileageEntries
            .FirstOrDefaultAsync(mileageEntry => mileageEntry.Id == mileageEntryId, cancellationToken);

        if (mileageEntry == null)
        {
            return null;
        }

        string? correctedText = NormalizeOptionalText(request.CorrectedText);

        mileageEntry.RecordedAtUtc = request.RecordedAtUtc.ToUniversalTime();
        mileageEntry.OdometerReadingKm = request.OdometerReadingKm;
        mileageEntry.TripDistanceKm = request.TripDistanceKm;
        mileageEntry.CorrectedText = correctedText;
        mileageEntry.IsTextCorrected = IsCorrectedText(mileageEntry.RecognizedText, correctedText);
        mileageEntry.Location = MapToValueObject(request.Location);
        mileageEntry.Notes = NormalizeOptionalText(request.Notes);
        mileageEntry.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(mileageEntry);
    }

    /// <summary>
    /// Deletes an existing mileage entry.
    /// </summary>
    /// <param name="mileageEntryId">The unique identifier of the mileage entry to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True when the mileage entry was deleted; otherwise, false.</returns>
    public async Task<bool> DeleteMileageEntryAsync(
        Guid mileageEntryId,
        CancellationToken cancellationToken = default)
    {
        MileageEntry? mileageEntry = await noahDbContext.MileageEntries
            .FirstOrDefaultAsync(mileageEntry => mileageEntry.Id == mileageEntryId, cancellationToken);

        if (mileageEntry == null)
        {
            return false;
        }

        noahDbContext.MileageEntries.Remove(mileageEntry);

        await noahDbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Applies optional recorded-at filters to a mileage query.
    /// </summary>
    /// <param name="query">The query to filter.</param>
    /// <param name="fromUtc">The optional inclusive lower recorded-at boundary.</param>
    /// <param name="toUtc">The optional inclusive upper recorded-at boundary.</param>
    /// <returns>The filtered query.</returns>
    private static IQueryable<MileageEntry> ApplyDateFilters(
        IQueryable<MileageEntry> query,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        if (fromUtc.HasValue)
        {
            DateTimeOffset normalizedFromUtc = fromUtc.Value.ToUniversalTime();
            query = query.Where(mileageEntry => mileageEntry.RecordedAtUtc >= normalizedFromUtc);
        }

        if (toUtc.HasValue)
        {
            DateTimeOffset normalizedToUtc = toUtc.Value.ToUniversalTime();
            query = query.Where(mileageEntry => mileageEntry.RecordedAtUtc <= normalizedToUtc);
        }

        return query;
    }

    /// <summary>
    /// Trims optional text and stores blank values as null.
    /// </summary>
    /// <param name="value">The text value to normalize.</param>
    /// <returns>The trimmed value, or null when the value is blank.</returns>
    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    /// <summary>
    /// Determines whether corrected text differs from the original recognized text.
    /// </summary>
    /// <param name="recognizedText">The original recognized text.</param>
    /// <param name="correctedText">The user-corrected text.</param>
    /// <returns>True when the text was corrected; otherwise, false.</returns>
    private static bool IsCorrectedText(string? recognizedText, string? correctedText)
    {
        return correctedText != null &&
               !string.Equals(recognizedText, correctedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Maps a nullable coordinate contract model to the domain value object.
    /// </summary>
    /// <param name="coordinate">The coordinate DTO to map.</param>
    /// <returns>The domain coordinate value object, or null when no coordinate was supplied.</returns>
    private static GeoCoordinate? MapToValueObject(GeoCoordinateDto? coordinate)
    {
        if (coordinate == null)
        {
            return null;
        }

        return new GeoCoordinate
        {
            Latitude = coordinate.Latitude,
            Longitude = coordinate.Longitude,
            AccuracyMeters = coordinate.AccuracyMeters
        };
    }

    /// <summary>
    /// Maps a mileage entry entity to its contract model.
    /// </summary>
    /// <param name="mileageEntry">The mileage entry entity to map.</param>
    /// <returns>The mileage entry DTO.</returns>
    private static MileageEntryDto MapToDto(MileageEntry mileageEntry)
    {
        return new MileageEntryDto(
            mileageEntry.Id,
            mileageEntry.RecordedAtUtc,
            mileageEntry.OdometerReadingKm,
            mileageEntry.TripDistanceKm,
            (MileageEntrySourceDto)mileageEntry.Source,
            mileageEntry.SourceImagePath,
            mileageEntry.RecognizedText,
            mileageEntry.CorrectedText,
            mileageEntry.IsTextCorrected,
            MapToDto(mileageEntry.Location),
            mileageEntry.Notes,
            mileageEntry.CreatedAtUtc,
            mileageEntry.UpdatedAtUtc);
    }

    /// <summary>
    /// Maps a domain coordinate value object to its contract model.
    /// </summary>
    /// <param name="coordinate">The coordinate value object to map.</param>
    /// <returns>The coordinate DTO, or null when no coordinate is available.</returns>
    private static GeoCoordinateDto? MapToDto(GeoCoordinate? coordinate)
    {
        if (coordinate == null)
        {
            return null;
        }

        return new GeoCoordinateDto(
            coordinate.Latitude,
            coordinate.Longitude,
            coordinate.AccuracyMeters);
    }
}
