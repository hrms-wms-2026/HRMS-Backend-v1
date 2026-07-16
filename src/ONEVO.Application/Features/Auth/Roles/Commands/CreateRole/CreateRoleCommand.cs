using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Roles.Commands.CreateRole;

public record CreateRoleCommand(
    string Name,
    string? Description,
    IReadOnlyList<Guid>? PermissionIds) : IRequest<Result<RoleDetailDto>>;
