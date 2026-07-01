using Client.Models;
using NOAH.Contracts.Common;
using NOAH.Contracts.Mileage;

namespace Client.Services;

public sealed class ApiMileageRepository(NoahApiClient apiClient) : IMileageRepository
{
    public async Task<double> GetOdometerAsync(CancellationToken cancellationToken = default)
    {
        MileageEntryDto latestEntry = await apiClient.GetAsync<MileageEntryDto>("mileage/latest", cancellationToken);
        return (double)latestEntry.OdometerReadingKm;
    }

    public async Task<double> GetLastTripKmAsync(CancellationToken cancellationToken = default)
    {
        MileageEntryDto latestEntry = await apiClient.GetAsync<MileageEntryDto>("mileage/latest", cancellationToken);
        return (double)(latestEntry.TripDistanceKm ?? 0m);
    }

    public async Task<double> GetThisMonthKmAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset fromUtc = new(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        string query = $"mileage/summary?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(now.ToString("O"))}";
        MileageSummaryDto summary = await apiClient.GetAsync<MileageSummaryDto>(query, cancellationToken);
        return (double)summary.TotalTripDistanceKm;
    }

    public async Task<DateTime> GetLastRecordedAtAsync(CancellationToken cancellationToken = default)
    {
        MileageEntryDto latestEntry = await apiClient.GetAsync<MileageEntryDto>("mileage/latest", cancellationToken);
        return latestEntry.RecordedAtUtc.LocalDateTime;
    }

    public async Task<IReadOnlyList<MileageEntry>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MileageEntryDto> entries = await apiClient.GetAsync<IReadOnlyList<MileageEntryDto>>($"mileage?take={Math.Max(1, count)}", cancellationToken);
        return entries.Select(Map).ToList();
    }

    public async Task<MileageEntry> SaveAsync(MileageEntry entry, CancellationToken cancellationToken = default)
    {
        NOAH.Contracts.Common.GeoCoordinateDto? location = entry.LocationLatitude.HasValue && entry.LocationLongitude.HasValue
            ? new NOAH.Contracts.Common.GeoCoordinateDto(entry.LocationLatitude.Value, entry.LocationLongitude.Value, entry.LocationAccuracyMeters)
            : null;

        if (entry.Id == Guid.Empty)
        {
            CreateMileageEntryRequest request = new(
                entry.RecordedAtUtc,
                (decimal)entry.Odometer,
                entry.TripKm <= 0 ? null : (decimal)entry.TripKm,
                entry.Source,
                entry.SourceImagePath,
                entry.RecognizedText,
                entry.CorrectedText,
                location,
                string.IsNullOrWhiteSpace(entry.Note) ? null : entry.Note.Trim());

            MileageEntryDto created = await apiClient.PostAsync<CreateMileageEntryRequest, MileageEntryDto>("mileage", request, cancellationToken);
            return Map(created);
        }

        UpdateMileageEntryRequest updateRequest = new(
            entry.RecordedAtUtc,
            (decimal)entry.Odometer,
            entry.TripKm <= 0 ? null : (decimal)entry.TripKm,
            entry.CorrectedText,
            location,
            string.IsNullOrWhiteSpace(entry.Note) ? null : entry.Note.Trim());

        MileageEntryDto updated = await apiClient.PutAsync<UpdateMileageEntryRequest, MileageEntryDto>($"mileage/{entry.Id:D}", updateRequest, cancellationToken);
        return Map(updated);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => apiClient.DeleteAsync($"mileage/{id:D}", cancellationToken);

    private static MileageEntry Map(MileageEntryDto entryDto)
    {
        return new MileageEntry
        {
            Id = entryDto.Id,
            RecordedAtUtc = entryDto.RecordedAtUtc,
            Odometer = (double)entryDto.OdometerReadingKm,
            TripKm = (double)(entryDto.TripDistanceKm ?? 0m),
            Source = entryDto.Source,
            SourceImagePath = entryDto.SourceImagePath,
            RecognizedText = entryDto.RecognizedText,
            CorrectedText = entryDto.CorrectedText,
            LocationLatitude = entryDto.Location?.Latitude,
            LocationLongitude = entryDto.Location?.Longitude,
            LocationAccuracyMeters = entryDto.Location?.AccuracyMeters,
            Note = entryDto.Notes ?? string.Empty,
            CreatedAtUtc = entryDto.CreatedAtUtc,
            UpdatedAtUtc = entryDto.UpdatedAtUtc
        };
    }
}
