using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;


namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.AdminListTenantRoles;

public sealed record AdminListTenantRolesQuery(Guid TenantId)
    : IRequest<Result<IReadOnlyList<RoleSummaryDto>>>;
