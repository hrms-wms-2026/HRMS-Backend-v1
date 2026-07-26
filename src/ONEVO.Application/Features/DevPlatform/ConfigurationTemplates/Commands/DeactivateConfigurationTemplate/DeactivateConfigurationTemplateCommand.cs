using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.DeactivateConfigurationTemplate;

public sealed record DeactivateConfigurationTemplateCommand(Guid TemplateId)
    : IRequest<Result<ConfigurationTemplateDto>>;
