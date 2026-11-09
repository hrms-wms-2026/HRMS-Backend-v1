using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class OfficeProjectProvisionerRlsIntegrationTests
{
    [Fact]
    public async Task EnsureAsync_AsOnevoApp_CreatesTheTenantsOfficeProject()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        var scopes = harness.BuildAppScopeFactory();

        await using var scope = scopes.CreateAsyncScope();
        var context = await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
            .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, DateOnly.FromDateTime(DateTime.UtcNow));

        await using var admin = harness.AdminContext();
        var office = await admin.Projects.SingleAsync(p => p.TenantId == harness.TenantId && p.SystemPurpose == ProjectSystemPurposes.Office);
        Assert.Equal(context.ProjectId, office.Id);
        Assert.Equal(hr.Id, office.LeadId);
    }

    [Fact]
    public async Task EnsureAsync_AsOnevoApp_CalledTwice_ReturnsTheSameProject()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        var scopes = harness.BuildAppScopeFactory();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid first, second;
        await using (var scope = scopes.CreateAsyncScope())
            first = (await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
                .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, today)).ProjectId;
        await using (var scope = scopes.CreateAsyncScope())
            second = (await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
                .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, today)).ProjectId;

        Assert.Equal(first, second);
        await using var admin = harness.AdminContext();
        Assert.Equal(1, await admin.Projects.CountAsync(p => p.TenantId == harness.TenantId && p.SystemPurpose == ProjectSystemPurposes.Office));
    }

    [Fact]
    public async Task EnsureAsync_WithALaterTargetDate_ExtendsTheProjectAndItsRootObjective()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        var scopes = harness.BuildAppScopeFactory();
        var later = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(3);

        Guid projectId;
        await using (var scope = scopes.CreateAsyncScope())
            projectId = (await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
                .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, DateOnly.FromDateTime(DateTime.UtcNow))).ProjectId;
        await using (var scope = scopes.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
                .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, later);

        await using var admin = harness.AdminContext();
        Assert.Equal(later, (await admin.Projects.SingleAsync(p => p.Id == projectId)).TargetDate);
        Assert.Contains(await admin.Objectives.Where(o => o.ProjectId == projectId).Select(o => o.EndDate).ToListAsync(),
            end => end >= later);
    }

    [Fact]
    public async Task EnsureAsync_ConcurrentFirstCalls_CreateOneOfficeProject()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        var scopes = harness.BuildAppScopeFactory();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var gate = new SemaphoreSlim(0);

        var calls = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await gate.WaitAsync();
            await using var scope = scopes.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
                .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, today)).ProjectId;
        })).ToList();
        gate.Release(calls.Count);
        var ids = await Task.WhenAll(calls);

        Assert.Single(ids.Distinct());
        await using var admin = harness.AdminContext();
        Assert.Equal(1, await admin.Projects.CountAsync(p => p.TenantId == harness.TenantId && p.SystemPurpose == ProjectSystemPurposes.Office));
    }

    [Fact]
    public async Task EnsureAsync_NewOfficeProject_HasTheSystemMeetingCategory()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        var scopes = harness.BuildAppScopeFactory();

        await using var scope = scopes.CreateAsyncScope();
        var context = await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
            .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, DateOnly.FromDateTime(DateTime.UtcNow));

        await using var admin = harness.AdminContext();
        var meeting = await admin.TaskCategories.SingleAsync(c => c.ProjectId == context.ProjectId && c.SystemKey == "meeting");
        Assert.Equal("Meeting", meeting.Name);
        Assert.True(context.CategoryIds.ContainsKey("meeting"));
    }

    [Fact]
    public async Task EnsureAsync_NewOfficeProject_GivesTheSystemCategoriesTheirKinds()
    {
        await using var harness = await OfficeProjectDb.CreateAsync();
        var hr = await harness.SeedEmployeeAsync(harness.LegalEntityId);
        var scopes = harness.BuildAppScopeFactory();

        await using var scope = scopes.CreateAsyncScope();
        var context = await scope.ServiceProvider.GetRequiredService<IOfficeProjectProvisioner>()
            .EnsureAsync(harness.TenantId, harness.LegalEntityId, hr.UserId, hr.Id, DateOnly.FromDateTime(DateTime.UtcNow));

        await using var admin = harness.AdminContext();
        var rows = await admin.TaskCategories.Where(c => c.ProjectId == context.ProjectId).OrderBy(c => c.DisplayOrder)
            .Select(c => new { c.Name, c.SystemKey, c.Kind }).ToListAsync();
        Assert.Equal(
            new[] { ("Onboarding", (string?)null, "checklist"), ("Offboarding", null, "checklist"), ("Meeting", "meeting", "meeting") },
            rows.Select(r => (r.Name, r.SystemKey, r.Kind)));
        Assert.True(context.CategoryIds.ContainsKey("onboarding") && context.CategoryIds.ContainsKey("offboarding"));
    }
}
