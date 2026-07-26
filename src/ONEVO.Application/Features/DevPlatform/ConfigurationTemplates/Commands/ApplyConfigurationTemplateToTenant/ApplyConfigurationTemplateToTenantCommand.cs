using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.ApplyConfigurationTemplateToTenant;

public sealed record ApplyConfigurationTemplateToTenantCommand(
    Guid TenantId,
    Guid TemplateId,
    bool ForceUpdate,
    Guid AppliedById) : IRequest<Result<ApplyConfigurationTemplateResultDto>>;
