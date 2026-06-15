using Api.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NOAH.Contracts.Enums;
using NOAH.Contracts.Search;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

/// <summary>
/// Exposes cross-entity search endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class SearchController(ISearchService searchService) : ControllerBase
{
    private const int DefaultTake = 25;
    private const int MaximumTake = 100;
    private const int MaximumQueryLength = 500;

    /// <summary>
    /// Searches across supported NOAH entities using query string parameters.
    /// </summary>
    /// <param name="query">The optional search text.</param>
    /// <param name="types">The optional entity types to include.</param>
    /// <param name="fromUtc">The optional inclusive lower relevant-date boundary.</param>
    /// <param name="toUtc">The optional inclusive upper relevant-date boundary.</param>
    /// <param name="skip">The number of matching results to skip.</param>
    /// <param name="take">The maximum number of matching results to return.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The matching search results.</returns>
    [HttpGet]
    public async Task<ActionResult<SearchResponse>> SearchAsync(
        [FromQuery(Name = "q")] string? query,
        [FromQuery] SearchResultTypeDto[]? types,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int skip = 0,
        [FromQuery] int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        SearchRequest request = new(
            query,
            types is { Length: > 0 } ? types : null,
            fromUtc,
            toUtc,
            skip,
            take);

        return await SearchWithRequestAsync(request, cancellationToken);
    }

    /// <summary>
    /// Searches across supported NOAH entities using a JSON request body.
    /// </summary>
    /// <param name="request">The search criteria.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The matching search results.</returns>
    [HttpPost]
    public async Task<ActionResult<SearchResponse>> SearchWithBodyAsync(
        [FromBody] SearchRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateSearchRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        SearchRequest normalizedRequest = NormalizeSearchRequest(request!);
        SearchResponse response = await searchService.SearchAsync(normalizedRequest, cancellationToken);

        return Ok(response);
    }

    private async Task<ActionResult<SearchResponse>> SearchWithRequestAsync(
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateSearchRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        SearchRequest normalizedRequest = NormalizeSearchRequest(request);
        SearchResponse response = await searchService.SearchAsync(normalizedRequest, cancellationToken);

        return Ok(response);
    }

    private static SearchRequest NormalizeSearchRequest(SearchRequest request)
    {
        SearchResultTypeDto[]? normalizedTypes = request.Types?
            .Distinct()
            .ToArray();

        return request with
        {
            Query = request.Query?.Trim() ?? string.Empty,
            Types = normalizedTypes is { Length: > 0 } ? normalizedTypes : null,
            FromUtc = request.FromUtc?.ToUniversalTime(),
            ToUtc = request.ToUtc?.ToUniversalTime(),
            Take = request.Take <= 0 ? DefaultTake : request.Take
        };
    }

    private static Dictionary<string, string[]> ValidateSearchRequest(SearchRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        if (request.Query is { Length: > MaximumQueryLength })
        {
            validationErrors[nameof(request.Query)] = [$"Query cannot be longer than {MaximumQueryLength} characters."];
        }

        if (request.Skip < 0)
        {
            validationErrors[nameof(request.Skip)] = ["Skip must be greater than or equal to zero."];
        }

        if (request.Take < 0 || request.Take > MaximumTake)
        {
            validationErrors[nameof(request.Take)] =
                [$"Take must be between 0 and {MaximumTake}. Use 0 to request the default page size."];
        }

        if (request.FromUtc.HasValue &&
            request.ToUtc.HasValue &&
            request.FromUtc.Value.ToUniversalTime() > request.ToUtc.Value.ToUniversalTime())
        {
            validationErrors[nameof(request.FromUtc)] = ["From UTC must be before or equal to To UTC."];
        }

        if (request.Types != null)
        {
            SearchResultTypeDto[] invalidTypes = request.Types
                .Where(type => !Enum.IsDefined(type))
                .Distinct()
                .ToArray();

            if (invalidTypes.Length > 0)
            {
                validationErrors[nameof(request.Types)] = ["One or more search result types are invalid."];
            }
        }

        return validationErrors;
    }

    private static ModelStateDictionary ToModelStateDictionary(
        Dictionary<string, string[]> validationErrors)
    {
        ModelStateDictionary modelStateDictionary = new();

        foreach (KeyValuePair<string, string[]> validationError in validationErrors)
        {
            foreach (string errorMessage in validationError.Value)
            {
                modelStateDictionary.AddModelError(validationError.Key, errorMessage);
            }
        }

        return modelStateDictionary;
    }
}
