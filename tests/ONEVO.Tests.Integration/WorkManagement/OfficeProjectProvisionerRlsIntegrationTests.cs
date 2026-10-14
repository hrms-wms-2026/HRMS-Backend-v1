using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

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
}
