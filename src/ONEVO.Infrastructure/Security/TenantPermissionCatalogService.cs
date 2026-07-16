using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

using ONEVO.Application.Features.Auth.Permission;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;

namespace ONEVO.Infrastructure.Security;

public sealed class TenantPermissionCatalogService : ITenantPermissionCatalogService
{
    private readonly IModuleEntitlementService _entitlements;
    private readonly ApplicationDbContext _db;

    public TenantPermissionCatalogService(
        IModuleEntitlementService entitlements,
        ApplicationDbContext db)
    {
        _entitlements = entitlements;
        _db = db;
    }

    public async Task<PermissionCatalogDto> GetCatalogAsync(Guid tenantId, CancellationToken ct = default)
    {
        var assignable = await _entitlements.GetAssignablePermissionsForTenantAsync(tenantId, ct);
        var assignableDtos = assignable
            .Select(p => new PermissionCatalogItemDto(
                p.Id,
                p.Code,
                p.Description,
                p.Module,
                IsAssignable: true,
                IsUniversal: false))
            .ToList();

        var universalCodes = ModuleAutoGrants.ByModule.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).ToList();
        var dbUniversalRows = await _db.Permissions
            .AsNoTracking()
            .Where(p => universalCodes.Contains(p.Code))
            .ToListAsync(ct);
        var dbByCode = dbUniversalRows.ToDictionary(p => p.Code, StringComparer.Ordinal);

        var universalDtos = new List<PermissionCatalogItemDto>(universalCodes.Count);
        foreach (var code in universalCodes)
        {
            if (dbByCode.TryGetValue(code, out var row))
            {
                universalDtos.Add(new PermissionCatalogItemDto(
                    row.Id,
                    row.Code,
                    row.Description,
                    row.Module,
                    IsAssignable: false,
                    IsUniversal: true));
            }
            else
            {
                universalDtos.Add(new PermissionCatalogItemDto(
                    null,
                    code,
                    FallbackDescription(code),
                    FallbackModule(code),
                    IsAssignable: false,
                    IsUniversal: true));
            }
        }

        return new PermissionCatalogDto(assignableDtos, universalDtos);
    }

    private static string FallbackModule(string code)
    {
        var i = code.IndexOf(':');
        return i > 0 ? code[..i] : "platform";
    }

    private static string FallbackDescription(string code)
        => $"Automatically granted to all active users ({code}).";
}
