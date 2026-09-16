using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using TimeAttendanceWorkMode = ONEVO.Domain.Features.TimeAttendance.Entities.WorkMode;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.WorkModes;

public class EfWorkModeRepositoryTests
{
    private static ApplicationDbContext BuildInMemoryDb()
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
    public async Task ListByLegalEntityAsync_ExcludesInactive_ByDefault()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.TimeAttendanceWorkModes.AddRange(
            new TimeAttendanceWorkMode
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityId,
                Name = "Remote", IsActive = true, CreatedAt = now, UpdatedAt = now
            },
            new TimeAttendanceWorkMode
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityId,
                Name = "Old Field", IsActive = false, CreatedAt = now, UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var repo = new EfWorkModeRepository(db);
        var active = await repo.ListByLegalEntityAsync(tenantId, legalEntityId, includeInactive: false);
        var all = await repo.ListByLegalEntityAsync(tenantId, legalEntityId, includeInactive: true);

        Assert.Single(active);
        Assert.Equal("Remote", active[0].Name);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task CountActiveAsync_CountsOnlyActiveRowsForThatLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.TimeAttendanceWorkModes.AddRange(
            new TimeAttendanceWorkMode { Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityId, Name = "Remote", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new TimeAttendanceWorkMode { Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityId, Name = "Hybrid", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new TimeAttendanceWorkMode { Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityId, Name = "Retired", IsActive = false, CreatedAt = now, UpdatedAt = now },
            new TimeAttendanceWorkMode { Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = otherLegalEntityId, Name = "Onsite", IsActive = true, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var repo = new EfWorkModeRepository(db);
        var count = await repo.CountActiveAsync(tenantId, legalEntityId);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task NameExistsAsync_IsCaseInsensitive_AndScopedToLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var existingId = Guid.NewGuid();
        db.TimeAttendanceWorkModes.Add(new TimeAttendanceWorkMode
        {
            Id = existingId, TenantId = tenantId, LegalEntityId = legalEntityId,
            Name = "Remote", IsActive = true, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var repo = new EfWorkModeRepository(db);

        Assert.True(await repo.NameExistsAsync(tenantId, legalEntityId, "remote", excludingId: null));
        Assert.False(await repo.NameExistsAsync(tenantId, legalEntityId, "remote", excludingId: existingId));
        Assert.False(await repo.NameExistsAsync(tenantId, legalEntityId, "onsite", excludingId: null));
    }
}
