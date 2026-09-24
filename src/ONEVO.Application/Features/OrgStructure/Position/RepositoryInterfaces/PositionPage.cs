using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public sealed record PositionPage(
    IReadOnlyList<Position> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
