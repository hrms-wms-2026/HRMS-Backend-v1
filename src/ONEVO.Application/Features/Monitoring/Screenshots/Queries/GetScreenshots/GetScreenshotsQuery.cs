using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetScreenshots;

public record GetScreenshotsQuery(
    Guid? EmployeeId,
    DateOnly? From,
    DateOnly? To,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<EvidenceAssetDto>>>;
