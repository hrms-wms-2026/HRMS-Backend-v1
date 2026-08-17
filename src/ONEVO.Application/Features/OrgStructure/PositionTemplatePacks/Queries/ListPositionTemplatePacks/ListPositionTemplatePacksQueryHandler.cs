using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.DTOs;
using ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.Mappers;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.Queries.ListPositionTemplatePacks;

/// <summary>Tenant-facing read of active system/global position_template packs for the Position
/// screen template picker. Reuses the Configuration Template Manager's canonical
/// configuration_templates store (see [[developer-platform/modules/configuration-template-manager/end-to-end-logic]])
/// - it is not the admin catalog endpoint and never accepts tenantId from the caller.</summary>
public sealed class ListPositionTemplatePacksQueryHandler
    : IRequestHandler<ListPositionTemplatePacksQuery, Result<PositionTemplatePackListResponseDto>>
{
    /// <summary>Safety bound on the underlying configuration_templates read. Phase 1 seeds a small,
    /// fixed set of system packs; this is not exposed to the caller as pagination.</summary>
    private const int MaxTemplates = 200;

    private readonly IConfigurationTemplateRepository _templates;
    private readonly IModuleEntitlementService _entitlements;
    private readonly ITenantContext _tenantContext;

    public ListPositionTemplatePacksQueryHandler(
        IConfigurationTemplateRepository templates,
        IModuleEntitlementService entitlements,
        ITenantContext tenantContext)
    {
        _templates = templates;
        _entitlements = entitlements;
        _tenantContext = tenantContext;
    }

    public async Task<Result<PositionTemplatePackListResponseDto>> Handle(
        ListPositionTemplatePacksQuery request,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return Result<PositionTemplatePackListResponseDto>.Forbidden("Tenant context missing.");
        }

        var templates = await _templates.ListAsync(
            ConfigurationTemplate.TypePositionTemplate, activeOnly: true, industryProfileTag: null,
            skip: 0, take: MaxTemplates, ct);

        var items = new List<PositionTemplatePackDto>(templates.Count);
        foreach (var template in templates)
        {
            var isEntitled = await IsTenantEntitledAsync(tenantId, template.ModuleKeysJson, ct);
            if (!isEntitled)
                continue;

            if (!PositionTemplatePackMapper.TryMap(template, out var dto))
            {
                // A seeded/global template failed schema validation. This is configuration
                // corruption, not a per-tenant condition - fail safely rather than return a
                // partial or malformed list, and never leak the raw parse/validation detail.
                return Result<PositionTemplatePackListResponseDto>.Failure(
                    "Position template configuration is invalid. Contact support.", 500);
            }

            items.Add(dto!);
        }

        return Result<PositionTemplatePackListResponseDto>.Success(new PositionTemplatePackListResponseDto(items));
    }

    private async Task<bool> IsTenantEntitledAsync(Guid tenantId, string moduleKeysJson, CancellationToken ct)
    {
        var moduleKeys = DeserializeModuleKeys(moduleKeysJson);
        foreach (var moduleKey in moduleKeys)
        {
            var enabled = await _entitlements.IsModuleEnabledAsync(tenantId, moduleKey, ct);
            if (!enabled)
                return false;
        }

        return true;
    }

    private static List<string> DeserializeModuleKeys(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            var keys = JsonSerializer.Deserialize<List<string>>(json);
            return keys ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}
