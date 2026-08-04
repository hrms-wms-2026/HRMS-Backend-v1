namespace ONEVO.Api.Contracts.Common;

public sealed record PagedResultViewModel<T>(
    IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasNext, bool HasPrevious);
