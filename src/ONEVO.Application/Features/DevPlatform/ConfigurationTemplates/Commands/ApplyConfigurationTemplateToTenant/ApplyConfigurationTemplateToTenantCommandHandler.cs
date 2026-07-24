using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Helpers;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Mappers;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.ApplyConfigurationTemplateToTenant;

public sealed class ApplyConfigurationTemplateToTenantCommandHandler
    : IRequestHandler<ApplyConfigurationTemplateToTenantCommand, Result<ApplyConfigurationTemplateResultDto>>
{
    private readonly ITenantRepository _tenants;
    private readonly IConfigurationTemplateRepository _templates;
    private readonly ITenantConfigurationTemplateApplicationRepository _applications;
    private readonly IModuleEntitlementService _entitlements;
    private readonly IUnitOfWork _unitOfWork;

    public ApplyConfigurationTemplateToTenantCommandHandler(
        ITenantRepository tenants,
        IConfigurationTemplateRepository templates,
        ITenantConfigurationTemplateApplicationRepository applications,
        IModuleEntitlementService entitlements,
        IUnitOfWork unitOfWork)
    {
        _tenants = tenants;
        _templates = templates;
        _applications = applications;
        _entitlements = entitlements;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ApplyConfigurationTemplateResultDto>> Handle(
        ApplyConfigurationTemplateToTenantCommand request,
        CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
        {
            return Result<ApplyConfigurationTemplateResultDto>.NotFound("Tenant not found.");
        }

        var template = await _templates.GetByIdAsync(request.TemplateId, ct);
        if (template is null)
        {
            return Result<ApplyConfigurationTemplateResultDto>.NotFound("Configuration template not found.");
        }

        if (!template.IsActive)
        {
            return Result<ApplyConfigurationTemplateResultDto>.Failure("This configuration template is not active.");
        }

        var requiredModuleKey = ConfigurationTemplateModuleRequirement.RequiredModuleKeyFor(template.TemplateType);
        if (requiredModuleKey is not null)
        {
            var isEntitled = await _entitlements.IsModuleEnabledAsync(request.TenantId, requiredModuleKey, ct);
            if (!isEntitled)
            {
                return Result<ApplyConfigurationTemplateResultDto>.Failure(
                    $"Module '{requiredModuleKey}' is not entitled for this tenant. Apply template is blocked.");
            }
        }

        // Downstream module payload execution (tenant_settings, positions, time_off_types,
        // monitoring_feature_toggles, app_allowlists, checklist_templates,
        // data_import_mapping_templates) is deferred to a later step. This foundation
        // writes only the immutable audit row below.
        var warnings = new List<string>
        {
            "Downstream module payload execution is deferred in this foundation step; only the audit application row was written."
        };

        var application = new TenantConfigurationTemplateApplication
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ConfigurationTemplateId = template.Id,
            TemplateType = template.TemplateType,
            AppliedVersion = template.Version,
            AppliedPayloadJson = template.PayloadJson,
            CustomPayloadJson = null,
            WarningsJson = ConfigurationTemplateMapper.SerializeStringList(warnings),
            Status = TenantConfigurationTemplateApplication.StatusApplied,
            AppliedById = request.AppliedById,
            AppliedAt = DateTimeOffset.UtcNow
        };

        await _applications.AddAsync(application, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ApplyConfigurationTemplateResultDto>.Success(
            new ApplyConfigurationTemplateResultDto(application.Id, application.AppliedVersion, warnings));
    }
}
