using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ApplyRoleTemplate;

public sealed record ApplyRoleTemplateCommand(
    Guid TenantId,
    Guid TemplateId,
    string? RoleNameOverride,
    bool ForceUpdate) : IRequest<Result<ApplyRoleTemplateResultDto>>;
