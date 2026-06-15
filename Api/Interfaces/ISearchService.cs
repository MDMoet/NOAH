using NOAH.Contracts.Search;

namespace Api.Interfaces;

/// <summary>
/// Coordinates cross-entity search across NOAH data.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches across supported NOAH entities.
    /// </summary>
    /// <param name="request">The search criteria.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The matching search results.</returns>
    Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default);
}
