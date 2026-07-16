using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateRoleTemplate;

public sealed record UpdateRoleTemplateCommand(
    Guid TemplateId,
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    IReadOnlyList<string> PermissionCodes,
    bool IsActive) : IRequest<Result<RoleTemplateDto>>;
