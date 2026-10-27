using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.FileProviders;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Services.CoreHr;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

/// <summary>The startup run pages the real tenants table. Paging must visit every tenant that
/// existed when the run started exactly once, even when tenants are created during the run.</summary>
public sealed class OfficeProjectReconciliationStartupPagingIntegrationTests
{
    /// <summary>Records reconciled tenants; on the first call it creates a brand-new tenant, the
    /// way a sign-up landing mid-run would.</summary>
    private sealed class RecordingReconciler(OfficeProjectDb harness) : IOfficeProjectReconciler
    {
        public List<Guid> Reconciled { get; } = [];

        public async Task<OfficeProjectReconcileOutcome> ReconcileAsync(Guid tenantId, CancellationToken ct = default)
        {
            Reconciled.Add(tenantId);
            if (Reconciled.Count == 1)
            {
                await using var admin = harness.AdminContext();
                admin.Tenants.Add(new Tenant
                {
                    Id = Guid.NewGuid(), Name = "Signed up mid-run", Slug = $"mid-{Guid.NewGuid():N}"[..20],
                    CompanySizeRange = "1-10", Status = TenantStatus.Active, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(5),
                });
                await admin.SaveChangesAsync(ct);
            }
            return OfficeProjectReconcileOutcome.AlreadyAligned;
        }

        public async Task<OfficeProjectReconcileOutcome?> TryReconcileAsync(Guid tenantId, CancellationToken ct = default) =>
            await ReconcileAsync(tenantId, ct);
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ONEVO.Tests.Integration";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task RunOnceAsync_TenantCreatedMidRun_EveryExistingTenantReconciledExactlyOnce()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        await using (var admin = harness.AdminContext())
        {
            for (var i = 1; i <= 4; i++)
            {
                var tenant = new Tenant
                {
                    Id = Guid.NewGuid(), Name = $"Existing {i}", Slug = $"existing-{Guid.NewGuid():N}"[..20],
                    CompanySizeRange = "1-10", Status = TenantStatus.Active,
                };
                admin.Tenants.Add(tenant);
                await admin.SaveChangesAsync();
                await admin.Tenants.Where(t => t.Id == tenant.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, DateTimeOffset.UtcNow.AddDays(-i)));
            }
        }
        List<Guid> existing;
        await using (var admin = harness.AdminContext())
            existing = await admin.Tenants.Where(t => t.Status != TenantStatus.Suspended && t.Status != TenantStatus.Cancelled)
                .Select(t => t.Id).ToListAsync();

        var recorder = new RecordingReconciler(harness);
        var provider = harness.BuildAppServices(services => services.AddSingleton<IOfficeProjectReconciler>(recorder));
        var service = new OfficeProjectReconciliationStartupService(
            provider, new DevelopmentEnvironment(), NullLogger<OfficeProjectReconciliationStartupService>.Instance, pageSize: 2);

        await service.RunOnceAsync(CancellationToken.None);

        foreach (var tenantId in existing)
            Assert.Equal(1, recorder.Reconciled.Count(id => id == tenantId));
    }
}
