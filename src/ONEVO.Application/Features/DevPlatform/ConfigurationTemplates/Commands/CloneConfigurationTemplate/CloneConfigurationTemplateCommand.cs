using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CloneConfigurationTemplate;

public sealed record CloneConfigurationTemplateCommand(Guid TemplateId, Guid CreatedById)
    : IRequest<Result<ConfigurationTemplateDto>>;
