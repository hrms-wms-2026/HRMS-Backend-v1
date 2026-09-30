using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EfReleaseCalendarRepositoryReadsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task ListForRecipient_ReturnsActiveEntriesInTheWindowForThatUser_WithVersionAndProjectNames_EarliestFirst()
    {
        await using var db = BuildInMemoryDb();
        var project = new Project { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Website", Identifier = "WEB", IsActive = true };
        var v1 = new ProjectVersion { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = project.Id, Name = "v2.0" };
        var v2 = new ProjectVersion { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = project.Id, Name = "v2.1" };
        db.Projects.Add(project);
        db.ProjectVersions.AddRange(v1, v2);
        db.ReleaseCalendarEntries.AddRange(
            Entry(project.Id, v2.Id, _userId, "2026-10-05"),                                   // in window, later
            Entry(project.Id, v1.Id, _userId, "2026-10-01", notes: "Freeze at noon"),          // in window, earlier
            Entry(project.Id, v1.Id, _userId, "2026-12-01"),                                   // outside window
            Entry(project.Id, v1.Id, Guid.NewGuid(), "2026-10-02"),                            // other user
            Entry(project.Id, v1.Id, _userId, "2026-10-03", isActive: false));                 // inactive
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfReleaseCalendarRepository(db)
            .ListForRecipientAsync(_tenantId, _userId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14));

        Assert.Equal(new[] { new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5) }, rows.Select(r => r.ScheduledDate).ToArray());
        Assert.Equal("v2.0", rows[0].VersionName);
        Assert.Equal("Website", rows[0].ProjectName);
        Assert.Equal("Freeze at noon", rows[0].Notes);
        Assert.Equal("v2.1", rows[1].VersionName);
    }

    private ReleaseCalendarEntry Entry(Guid projectId, Guid versionId, Guid userId, string date, string? notes = null, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, VersionId = versionId, RecipientUserId = userId,
        ScheduledDate = DateOnly.Parse(date), Notes = notes, IsActive = isActive
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
