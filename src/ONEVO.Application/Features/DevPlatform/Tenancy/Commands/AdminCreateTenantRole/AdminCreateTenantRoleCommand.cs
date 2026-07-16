using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;


namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminCreateTenantRole;

public sealed record AdminCreateTenantRoleCommand(
    Guid TenantId,
    string Name,
    string? Description,
    IReadOnlyList<Guid>? PermissionIds) : IRequest<Result<RoleDetailDto>>;
