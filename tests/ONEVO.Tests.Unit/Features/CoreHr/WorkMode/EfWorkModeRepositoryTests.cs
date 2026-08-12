using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

namespace ONEVO.Tests.Unit.Features.CoreHr.WorkMode;

public sealed class EfWorkModeRepositoryTests
{
    [Fact]
    public async Task ListActiveAsync_ExcludesInactiveWorkModes()
    {
        await using var db = BuildDb();
        db.WorkModes.AddRange(
            new ONEVO.Domain.Lookups.WorkMode { Id = 1, Code = "on_site", Label = "On-Site", IsActive = true },
            new ONEVO.Domain.Lookups.WorkMode { Id = 2, Code = "hybrid", Label = "Hybrid", IsActive = false });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfWorkModeRepository(db);
        var result = await repository.ListActiveAsync();

        result.Should().ContainSingle();
        result[0].Code.Should().Be("on_site");
    }

    [Fact]
    public async Task ListActiveAsync_OrdersByLabel_NotById()
    {
        await using var db = BuildDb();
        db.WorkModes.AddRange(
            new ONEVO.Domain.Lookups.WorkMode { Id = 1, Code = "zeta", Label = "Zeta Mode", IsActive = true },
            new ONEVO.Domain.Lookups.WorkMode { Id = 2, Code = "alpha", Label = "Alpha Mode", IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfWorkModeRepository(db);
        var result = await repository.ListActiveAsync();

        result.Select(w => w.Code).Should().Equal("alpha", "zeta");
    }

    [Fact]
    public async Task ListActiveAsync_ReturnsIntegerIds_MatchingSeededValues()
    {
        await using var db = BuildDb();
        db.WorkModes.Add(new ONEVO.Domain.Lookups.WorkMode { Id = 5, Code = "remote", Label = "Remote", IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfWorkModeRepository(db);
        var result = await repository.ListActiveAsync();

        result.Should().ContainSingle(w => w.Id == 5 && w.Code == "remote" && w.Label == "Remote");
    }

    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new Mock<IDateTimeProvider>();
        var currentUser = new Mock<ICurrentUser>();
        var publisher = new Mock<IPublisher>();
        var tenant = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenant.Object);
    }
}
