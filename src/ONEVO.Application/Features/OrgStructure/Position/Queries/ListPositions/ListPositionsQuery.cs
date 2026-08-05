using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListPositions;

public record ListPositionsQuery(
    Guid LegalEntityId,
    Guid? DepartmentId,
    string? Search,
    bool IncludeInactive,
    int Page,
    int PageSize,
    string SortBy,
    string SortDirection) : IRequest<Result<PositionPageResponse>>;
