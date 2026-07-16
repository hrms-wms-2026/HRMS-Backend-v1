using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.DevPlatform.Tenancy;

public sealed class TenantRoleStatusReader : ITenantRoleStatusReader
{
    private readonly ApplicationDbContext _db;

    public TenantRoleStatusReader(ApplicationDbContext db) => _db = db;

    public async Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var hasSystemRole = await _db.Roles
            .AnyAsync(r => r.TenantId == tenantId && r.IsSystem && !r.IsDeleted, ct);

        if (hasSystemRole)
            return new ProvisioningSectionStatus(
                Complete: true,
                Summary: new Dictionary<string, object?> { ["status"] = "configured" },
                MissingFields: [],
                BlockingErrors: [],
                Warnings: []);

        return NotConfiguredYetReaders.Build(
            section: "roles",
            code: "roles_not_configured",
            message: "No system roles have been seeded for this tenant.");
    }
}
