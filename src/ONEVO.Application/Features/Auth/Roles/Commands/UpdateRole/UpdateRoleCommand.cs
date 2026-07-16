using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Roles.Commands.UpdateRole;

public record UpdateRoleCommand(
    Guid RoleId,
    string Name,
    string? Description) : IRequest<Result<RoleSummaryDto>>;
