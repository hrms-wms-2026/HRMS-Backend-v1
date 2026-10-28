using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Services.CoreHr;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

public sealed class OfficeProjectReconcilerIntegrationTests
{
    private static async Task<OfficeProjectReconcileOutcome> ReconcileAsync(OfficeProjectDb harness)
    {
        await using var scope = harness.BuildAppScopeFactory().CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOfficeProjectReconciler>().ReconcileAsync(harness.TenantId);
    }

    private static async Task<Project?> OfficeAsync(OfficeProjectDb harness)
    {
        await using var admin = harness.AdminContext();
        return await admin.Projects.SingleOrDefaultAsync(p => p.TenantId == harness.TenantId && p.SystemPurpose == ProjectSystemPurposes.Office);
    }

    private static async Task<bool> IsActiveMemberAsync(OfficeProjectDb harness, Guid projectId, Guid employeeId)
    {
        await using var admin = harness.AdminContext();
        return await admin.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.EmployeeId == employeeId && m.IsActive);
    }

    [Fact]
    public async Task NoOwnerEmployee_CreatesNothing()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        await harness.AssignRoleAsync(Guid.NewGuid(), role, DateTimeOffset.UtcNow.AddDays(-5));

        Assert.Equal(OfficeProjectReconcileOutcome.NoOwnerEmployee, await ReconcileAsync(harness));
        Assert.Null(await OfficeAsync(harness));
    }

    [Fact]
    public async Task OwnerWithEmployee_CreatesOfficeLedByOwner()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));

        Assert.Equal(OfficeProjectReconcileOutcome.Created, await ReconcileAsync(harness));
        var office = await OfficeAsync(harness);
        Assert.NotNull(office);
        Assert.Equal(owner.Id, office!.LeadId);
        Assert.True(await IsActiveMemberAsync(harness, office.Id, owner.Id));
    }

    [Fact]
    public async Task ExistingOfficeLedByHr_SwitchesLeadToOwner_KeepsHrMembership()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await using (var scope = harness.BuildAppScopeFactory().CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
                .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, DateOnly.FromDateTime(DateTime.UtcNow));
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));

        Assert.Equal(OfficeProjectReconcileOutcome.LeadSwitched, await ReconcileAsync(harness));
        var office = (await OfficeAsync(harness))!;
        Assert.Equal(owner.Id, office.LeadId);
        Assert.True(await IsActiveMemberAsync(harness, office.Id, owner.Id));
        Assert.True(await IsActiveMemberAsync(harness, office.Id, hr.Id));
        await using var admin = harness.AdminContext();
        var root = await admin.Objectives.SingleAsync(o => o.ProjectId == office.Id && o.IsDefault);
        Assert.Equal(owner.Id, root.OwnerId);
    }

    [Fact]
    public async Task SecondRun_IsAlreadyAligned_WithoutDuplicateMembership()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));

        await ReconcileAsync(harness);
        Assert.Equal(OfficeProjectReconcileOutcome.AlreadyAligned, await ReconcileAsync(harness));

        var office = (await OfficeAsync(harness))!;
        await using var admin = harness.AdminContext();
        Assert.Equal(1, await admin.ProjectMembers.CountAsync(m => m.ProjectId == office.Id && m.EmployeeId == owner.Id));
    }

    [Fact]
    public async Task ExpiredOwnerAssignment_IsIgnored()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var expired = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(expired.UserId, role, DateTimeOffset.UtcNow.AddDays(-30), expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        var current = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(current.UserId, role, DateTimeOffset.UtcNow.AddDays(-2));

        await ReconcileAsync(harness);

        Assert.Equal(current.Id, (await OfficeAsync(harness))!.LeadId);
    }

    [Fact]
    public async Task EarliestOwnerWithoutEmployee_FallsThroughToNextOwnerWithEmployee()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        await harness.AssignRoleAsync(Guid.NewGuid(), role, DateTimeOffset.UtcNow.AddDays(-30));
        var later = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(later.UserId, role, DateTimeOffset.UtcNow.AddDays(-2));

        await ReconcileAsync(harness);

        Assert.Equal(later.Id, (await OfficeAsync(harness))!.LeadId);
    }

    [Fact]
    public async Task OwnerWhoseOnlyEmployeeIsTerminated_IsNotEligible()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var terminated = await harness.SeedEmployeeAsync(harness.LegalEntityId, EmploymentStatusIds.Terminated);
        await harness.AssignRoleAsync(terminated.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));

        Assert.Equal(OfficeProjectReconcileOutcome.NoOwnerEmployee, await ReconcileAsync(harness));
        Assert.Null(await OfficeAsync(harness));
    }

    [Fact]
    public async Task OwnerEmployeeWithoutLegalEntity_UsesThePrimaryLegalEntity()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(legalEntityId: null);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));

        await ReconcileAsync(harness);

        Assert.Equal(harness.LegalEntityId, (await OfficeAsync(harness))!.OwningLegalEntityId);
    }

    [Fact]
    public async Task OwnerWithTwoEmployees_PrefersThePrimaryLegalEntity()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var userId = Guid.NewGuid();
        var secondary = await harness.SeedSecondaryLegalEntityAsync();
        await harness.SeedEmployeeAsync(secondary, userId: userId);
        var primaryEmployee = await harness.SeedEmployeeAsync(harness.LegalEntityId, userId: userId);
        await harness.AssignRoleAsync(userId, role, DateTimeOffset.UtcNow.AddDays(-5));

        await ReconcileAsync(harness);

        Assert.Equal(primaryEmployee.Id, (await OfficeAsync(harness))!.LeadId);
    }

    [Fact]
    public async Task SuspendedTenant_IsSkipped()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        await harness.SeedTenantStatusAsync(TenantStatus.Suspended);

        Assert.Equal(OfficeProjectReconcileOutcome.TenantInactive, await ReconcileAsync(harness));
    }

    [Fact]
    public async Task TryReconcileAsync_WhenReconcileThrows_ReturnsNullInsteadOfThrowing()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));
        await ReconcileAsync(harness);
        // Corrupt the project so ReconcileAsync fails: its root module is gone.
        await using (var admin = harness.AdminContext())
            await admin.Objectives.IgnoreQueryFilters()
                .Where(o => o.TenantId == harness.TenantId && o.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.IsDeleted, true));

        await using var scope = harness.BuildAppScopeFactory().CreateAsyncScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<IOfficeProjectReconciler>();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => reconciler.ReconcileAsync(harness.TenantId));
        Assert.Null(await reconciler.TryReconcileAsync(harness.TenantId));
    }

    [Fact]
    public async Task SoftDeletedOwnerMembership_IsRestored_NotDuplicated()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));
        await ReconcileAsync(harness);
        var office = (await OfficeAsync(harness))!;
        await using (var admin = harness.AdminContext())
            await admin.ProjectMembers.IgnoreQueryFilters()
                .Where(m => m.ProjectId == office.Id && m.EmployeeId == owner.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.IsDeleted, true)
                    .SetProperty(m => m.DeletedAt, DateTimeOffset.UtcNow)
                    .SetProperty(m => m.IsActive, false));

        await ReconcileAsync(harness);

        await using var verify = harness.AdminContext();
        var rows = await verify.ProjectMembers.IgnoreQueryFilters()
            .Where(m => m.ProjectId == office.Id && m.EmployeeId == owner.Id).ToListAsync();
        var row = Assert.Single(rows);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAt);
        Assert.True(row.IsActive);
        Assert.Null(row.RemovedAt);
    }

    /// <summary>Simulates losing the create race: an HR-led Ensure lands first, so the owner-led
    /// Ensure the reconciler issues returns the existing HR-led project.</summary>
    private sealed class HrWinsCreateRaceProvisioner(IOfficeProjectProvisioner inner, Guid hrUserId, Guid hrEmployeeId)
        : IOfficeProjectProvisioner
    {
        public async Task<OfficeProjectContext> EnsureAsync(
            Guid tenantId, Guid initialOwningLegalEntityId, Guid actingUserId, Guid actingEmployeeId,
            DateOnly targetDate, CancellationToken ct = default)
        {
            await inner.EnsureAsync(tenantId, initialOwningLegalEntityId, hrUserId, hrEmployeeId, targetDate, ct);
            return await inner.EnsureAsync(tenantId, initialOwningLegalEntityId, actingUserId, actingEmployeeId, targetDate, ct);
        }
    }

    [Fact]
    public async Task LostCreateRace_ToHrLedEnsure_StillEndsOwnerLed()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));
        var scopes = harness.BuildAppScopeFactory(services => services.AddScoped<IOfficeProjectProvisioner>(sp =>
            new HrWinsCreateRaceProvisioner(
                new OfficeProjectProvisioner(sp.GetRequiredService<IServiceScopeFactory>()), hr.UserId, hr.Id)));

        OfficeProjectReconcileOutcome outcome;
        await using (var scope = scopes.CreateAsyncScope())
            outcome = await scope.ServiceProvider.GetRequiredService<IOfficeProjectReconciler>().ReconcileAsync(harness.TenantId);

        Assert.Equal(OfficeProjectReconcileOutcome.LeadSwitched, outcome);
        var office = (await OfficeAsync(harness))!;
        Assert.Equal(owner.Id, office.LeadId);
        Assert.True(await IsActiveMemberAsync(harness, office.Id, owner.Id));
        await using var admin = harness.AdminContext();
        Assert.Equal(owner.Id, (await admin.Objectives.SingleAsync(o => o.ProjectId == office.Id && o.IsDefault)).OwnerId);
    }

    [Fact]
    public async Task PartiallyAppliedEarlierRun_NextRunConverges()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await using (var scope = harness.BuildAppScopeFactory().CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
                .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, DateOnly.FromDateTime(DateTime.UtcNow));
        var role = await harness.SeedOwnerRoleAsync();
        var owner = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        await harness.AssignRoleAsync(owner.UserId, role, DateTimeOffset.UtcNow.AddDays(-5));
        // An earlier run switched the lead and then failed before the root owner and membership steps.
        var officeId = (await OfficeAsync(harness))!.Id;
        await using (var admin = harness.AdminContext())
            await admin.Projects.Where(p => p.Id == officeId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LeadId, owner.Id));

        Assert.Equal(OfficeProjectReconcileOutcome.AlreadyAligned, await ReconcileAsync(harness));

        Assert.True(await IsActiveMemberAsync(harness, officeId, owner.Id));
        await using var verify = harness.AdminContext();
        Assert.Equal(owner.Id, (await verify.Objectives.SingleAsync(o => o.ProjectId == officeId && o.IsDefault)).OwnerId);
    }

    [Fact]
    public async Task EarliestOwnerWithNoResolvableCompany_FallsThroughToNextOwner()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var role = await harness.SeedOwnerRoleAsync();
        var secondary = await harness.SeedSecondaryLegalEntityAsync();
        await using (var admin = harness.AdminContext())
            await admin.LegalEntities.Where(le => le.Id == harness.LegalEntityId)
                .ExecuteUpdateAsync(s => s.SetProperty(le => le.IsActive, false));
        var noCompany = await harness.SeedEmployeeAsync(legalEntityId: null);
        await harness.AssignRoleAsync(noCompany.UserId, role, DateTimeOffset.UtcNow.AddDays(-30));
        var next = await harness.SeedEmployeeAsync(secondary);
        await harness.AssignRoleAsync(next.UserId, role, DateTimeOffset.UtcNow.AddDays(-2));

        Assert.Equal(OfficeProjectReconcileOutcome.Created, await ReconcileAsync(harness));
        Assert.Equal(next.Id, (await OfficeAsync(harness))!.LeadId);
    }
}
