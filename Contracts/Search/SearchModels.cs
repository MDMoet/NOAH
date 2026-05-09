using NOAH.Contracts.Enums;

namespace NOAH.Contracts.Search;

public sealed record SearchRequest(
    string Query,
    IReadOnlyList<SearchResultTypeDto>? Types,
    int Skip,
    int Take);

public sealed record SearchResultDto(
    Guid Id,
    SearchResultTypeDto Type,
    string Title,
    string? Preview,
    DateTimeOffset CreatedAtUtc);

public sealed record SearchResponse(
    IReadOnlyList<SearchResultDto> Results,
    int TotalCount);
