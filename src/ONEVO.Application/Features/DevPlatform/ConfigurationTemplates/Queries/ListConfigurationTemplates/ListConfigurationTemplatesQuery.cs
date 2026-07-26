using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.ListConfigurationTemplates;

public sealed record ListConfigurationTemplatesQuery(
    string? TemplateType,
    bool? ActiveOnly,
    string? IndustryProfileTag,
    int Page,
    int PageSize) : IRequest<Result<ConfigurationTemplateListResponseDto>>;
