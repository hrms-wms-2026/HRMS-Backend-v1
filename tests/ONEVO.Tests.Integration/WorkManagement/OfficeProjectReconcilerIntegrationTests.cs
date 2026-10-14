using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Lookups;
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
}
