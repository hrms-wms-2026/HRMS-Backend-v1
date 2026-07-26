using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Mappers;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.GetConfigurationTemplateDetail;

public sealed class GetConfigurationTemplateDetailQueryHandler
    : IRequestHandler<GetConfigurationTemplateDetailQuery, Result<ConfigurationTemplateDetailDto>>
{
    private readonly IConfigurationTemplateRepository _templates;
    private readonly ITenantConfigurationTemplateApplicationRepository _applications;

    public GetConfigurationTemplateDetailQueryHandler(
        IConfigurationTemplateRepository templates,
        ITenantConfigurationTemplateApplicationRepository applications)
    {
        _templates = templates;
        _applications = applications;
    }

    public async Task<Result<ConfigurationTemplateDetailDto>> Handle(
        GetConfigurationTemplateDetailQuery request,
        CancellationToken ct)
    {
        var template = await _templates.GetByIdAsync(request.TemplateId, ct);
        if (template is null)
        {
            return Result<ConfigurationTemplateDetailDto>.NotFound("Configuration template not found.");
        }

        var history = await _applications.ListByTemplateAsync(template.Id, ct);
        var historyDtos = history.Select(ConfigurationTemplateMapper.ToDto).ToList();

        return Result<ConfigurationTemplateDetailDto>.Success(
            new ConfigurationTemplateDetailDto(ConfigurationTemplateMapper.ToDto(template), historyDtos));
    }
}
