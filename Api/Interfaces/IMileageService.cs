using NOAH.Contracts.Mileage;

namespace Api.Interfaces;

/// <summary>
/// Coordinates mileage-entry persistence, history queries, and summaries.
/// </summary>
public interface IMileageService
{
    /// <summary>
    /// Creates a new mileage entry.
    /// </summary>
    /// <param name="request">The mileage details used to create the entry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The created mileage entry.</returns>
    Task<MileageEntryDto> CreateMileageEntryAsync(
        CreateMileageEntryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets mileage entries ordered from newest to oldest.
    /// </summary>
    /// <param name="fromUtc">The optional inclusive lower recorded-at boundary.</param>
    /// <param name="toUtc">The optional inclusive upper recorded-at boundary.</param>
    /// <param name="take">The maximum number of entries to return.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The matching mileage entries.</returns>
    Task<IReadOnlyList<MileageEntryDto>> GetMileageEntriesAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a mileage entry by its unique identifier.
    /// </summary>
    /// <param name="mileageEntryId">The unique identifier of the mileage entry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The mileage entry when found; otherwise, null.</returns>
    Task<MileageEntryDto?> GetMileageEntryByIdAsync(
        Guid mileageEntryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the newest mileage entry.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The latest mileage entry when one exists; otherwise, null.</returns>
    Task<MileageEntryDto?> GetLatestMileageEntryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregate mileage statistics for an optional recorded-at range.
    /// </summary>
    /// <param name="fromUtc">The optional inclusive lower recorded-at boundary.</param>
    /// <param name="toUtc">The optional inclusive upper recorded-at boundary.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The mileage summary for the requested range.</returns>
    Task<MileageSummaryDto> GetMileageSummaryAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully updates an existing mileage entry by replacing its editable fields.
    /// </summary>
    /// <param name="mileageEntryId">The unique identifier of the mileage entry to update.</param>
    /// <param name="request">The replacement mileage details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated mileage entry when found; otherwise, null.</returns>
    Task<MileageEntryDto?> UpdateMileageEntryAsync(
        Guid mileageEntryId,
        UpdateMileageEntryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an existing mileage entry.
    /// </summary>
    /// <param name="mileageEntryId">The unique identifier of the mileage entry to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True when the mileage entry was deleted; otherwise, false.</returns>
    Task<bool> DeleteMileageEntryAsync(Guid mileageEntryId, CancellationToken cancellationToken = default);

}
