using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintConfigurationTests
{
    [Fact]
    public void Sprint_DefaultsStatusToDraft()
    {
        var sprint = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(),
            Name = "Sprint 1", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
            CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.Equal(SprintStatuses.Draft, sprint.Status);
    }

    [Fact]
    public void Sprint_StatusIsConfiguredAsConcurrencyToken()
    {
        // WM-02: Status doubles as the optimistic-concurrency token for Start/Complete/Achieve/
        // Edit/Delete, mirroring WorkAreaChangeRequestConfiguration's existing pattern. Without
        // this, two concurrent requests both pass the in-memory status check and both persist.
        using var db = BuildInMemoryDb();
        var status = db.Model.FindEntityType(typeof(Sprint))!.FindProperty(nameof(Sprint.Status));

        Assert.NotNull(status);
        Assert.True(status!.IsConcurrencyToken);
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
