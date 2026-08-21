using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenantAuditLog;

public sealed record ListTenantAuditLogQuery(Guid TenantId, int Page, int PageSize)
    : IRequest<Result<TenantAuditLogListResponse>>;
