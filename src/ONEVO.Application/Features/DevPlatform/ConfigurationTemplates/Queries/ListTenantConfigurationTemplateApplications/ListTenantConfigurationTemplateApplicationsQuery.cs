using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.ListTenantConfigurationTemplateApplications;

public sealed record ListTenantConfigurationTemplateApplicationsQuery(
    Guid TenantId,
    int Page,
    int PageSize) : IRequest<Result<TenantConfigurationTemplateApplicationListResponseDto>>;
