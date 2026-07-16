using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Roles.Commands.AssignRolePermissions;

public record AssignRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyList<Guid> PermissionIds) : IRequest<Result<RoleDetailDto>>;
