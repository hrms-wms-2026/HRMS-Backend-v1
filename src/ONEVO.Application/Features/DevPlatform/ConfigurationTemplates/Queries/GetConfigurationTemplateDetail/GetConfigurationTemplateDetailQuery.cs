using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.GetConfigurationTemplateDetail;

public sealed record GetConfigurationTemplateDetailQuery(Guid TemplateId)
    : IRequest<Result<ConfigurationTemplateDetailDto>>;
