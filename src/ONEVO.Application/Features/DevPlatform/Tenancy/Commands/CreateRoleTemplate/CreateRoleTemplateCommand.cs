using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateRoleTemplate;

public sealed record CreateRoleTemplateCommand(
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    IReadOnlyList<string> PermissionCodes) : IRequest<Result<RoleTemplateDto>>;
