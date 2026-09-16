using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;
using ONEVO.Infrastructure.Services.TimeAttendance;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Services.TimeAttendance;

public class WorkModeSeederTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, new Mock<IDateTimeProvider>().Object),
            new SoftDeleteInterceptor(new Mock<IDateTimeProvider>().Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    [Fact]
    public async Task SeedDefaultsAsync_CreatesExactlyRemoteHybridOnsite_MarkedSystemSeeded()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var repo = new EfWorkModeRepository(db);
        var seeder = new WorkModeSeeder(repo, new FakeDateTimeProvider());

        await seeder.SeedDefaultsAsync(tenantId, legalEntityId, CancellationToken.None);

        var seeded = await repo.ListByLegalEntityAsync(tenantId, legalEntityId, includeInactive: false);
        Assert.Equal(3, seeded.Count);
        Assert.Equal(new[] { "Remote", "Hybrid", "Onsite" }, seeded.Select(w => w.Name).ToArray());
        Assert.All(seeded, w => Assert.True(w.IsSystemSeeded));
        Assert.All(seeded, w => Assert.True(w.WebEnabled));
    }
}
