using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Mappers;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.ListConfigurationTemplates;

public sealed class ListConfigurationTemplatesQueryHandler
    : IRequestHandler<ListConfigurationTemplatesQuery, Result<ConfigurationTemplateListResponseDto>>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly IConfigurationTemplateRepository _templates;

    public ListConfigurationTemplatesQueryHandler(IConfigurationTemplateRepository templates)
    {
        _templates = templates;
    }

    public async Task<Result<ConfigurationTemplateListResponseDto>> Handle(
        ListConfigurationTemplatesQuery request,
        CancellationToken ct)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var size = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var skip = (page - 1) * size;

        var templates = await _templates.ListAsync(
            request.TemplateType, request.ActiveOnly, request.IndustryProfileTag, skip, size, ct);
        var total = await _templates.CountAsync(
            request.TemplateType, request.ActiveOnly, request.IndustryProfileTag, ct);
        var items = templates.Select(ConfigurationTemplateMapper.ToDto).ToList();

        return Result<ConfigurationTemplateListResponseDto>.Success(
            ConfigurationTemplateMapper.ToListResponseDto(items, total, page, size));
    }
}
