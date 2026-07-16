using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;


namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminAssignTenantRolePermissions;

public sealed record AdminAssignTenantRolePermissionsCommand(
    Guid TenantId,
    Guid RoleId,
    IReadOnlyList<Guid> PermissionIds) : IRequest<Result<RoleDetailDto>>;
