using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenants;

public sealed record ListTenantsQuery(
    string? Search,
    string? Status,
    string? PlanCode,
    int Page,
    int PageSize) : IRequest<Result<TenantListResponseDto>>;
