using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class EfAttendanceReadRepositoryTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LegalEntityId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherLegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task AddBreak_PersistsTenantEmployeeTimesAndSafeDefaults()
    {
        await using var db = BuildInMemoryDb();
        var breakRecord = new BreakRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = Guid.NewGuid(),
            BreakStart = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero),
            BreakEnd = null,
            BreakType = null,
            AutoDetected = false,
            CreatedAt = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero)
        };
        var repository = new EfAttendanceReadRepository(db);

        await repository.AddBreakAsync(breakRecord);
        await repository.SaveChangesAsync();

        var persisted = await db.BreakRecords.SingleAsync(x => x.Id == breakRecord.Id);
        persisted.TenantId.Should().Be(TenantId);
        persisted.EmployeeId.Should().Be(breakRecord.EmployeeId);
        persisted.BreakStart.Should().Be(breakRecord.BreakStart);
        persisted.BreakEnd.Should().BeNull();
        persisted.BreakType.Should().BeNull();
        persisted.AutoDetected.Should().BeFalse();
    }

    [Fact]
    public async Task GetOpenBreakTracked_IsScopedToTenantEmployeeAndLocalDay()
    {
        await using var db = BuildInMemoryDb();
        var employeeId = Guid.NewGuid();
        var target = new BreakRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId,
            BreakStart = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero)
        };
        db.BreakRecords.AddRange(
            target,
            new BreakRecord
            {
                Id = Guid.NewGuid(), TenantId = OtherTenantId, EmployeeId = employeeId,
                BreakStart = target.BreakStart
            },
            new BreakRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = Guid.NewGuid(),
                BreakStart = target.BreakStart
            },
            new BreakRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId,
                BreakStart = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)
            },
            new BreakRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId,
                BreakStart = target.BreakStart, BreakEnd = target.BreakStart.AddMinutes(5)
            });
        await db.SaveChangesAsync();

        var result = await new EfAttendanceReadRepository(db).GetOpenBreakTrackedAsync(
            TenantId,
            employeeId,
            new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));

        result.Should().NotBeNull();
        result!.Id.Should().Be(target.Id);
        db.Entry(result).State.Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public async Task SumCompletedBreakMinutes_ClipsToLocalDayWindowAndFiltersScope()
    {
        await using var db = BuildInMemoryDb();
        var employeeId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 8, 20, 18, 30, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 21, 18, 30, 0, TimeSpan.Zero);
        db.BreakRecords.AddRange(
            new BreakRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId,
                BreakStart = from.AddMinutes(-30), BreakEnd = to.AddMinutes(30)
            },
            new BreakRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId,
                BreakStart = from.AddHours(-2), BreakEnd = from.AddHours(-1)
            },
            new BreakRecord
            {
                Id = Guid.NewGuid(), TenantId = OtherTenantId, EmployeeId = employeeId,
                BreakStart = from, BreakEnd = to
            });
        await db.SaveChangesAsync();

        var result = await new EfAttendanceReadRepository(db).SumCompletedBreakMinutesAsync(
            TenantId, employeeId, from, to);

        result.Should().Be(1440);
    }

    [Fact]
    public async Task ListEmployeeIdentities_IsBatchedAndFiltersTenantAndLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var requested = NewEmployee(TenantId, LegalEntityId, "EMP-001", "Jane", "Doe", Guid.NewGuid());
        var second = NewEmployee(TenantId, LegalEntityId, "EMP-002", "John", "Roe", null);
        var otherTenant = NewEmployee(OtherTenantId, LegalEntityId, "EMP-003", "Other", "Tenant", null);
        var otherEntity = NewEmployee(TenantId, OtherLegalEntityId, "EMP-004", "Other", "Entity", null);
        db.Employees.AddRange(requested, second, otherTenant, otherEntity);
        await db.SaveChangesAsync();

        var result = await new EfAttendanceReadRepository(db).ListEmployeeIdentitiesAsync(
            TenantId, LegalEntityId, [requested.Id, second.Id, otherTenant.Id, otherEntity.Id]);

        result.Keys.Should().BeEquivalentTo([requested.Id, second.Id]);
        result[requested.Id].DisplayName.Should().Be("Jane Doe");
        result[requested.Id].EmployeeNumber.Should().Be("EMP-001");
        result[requested.Id].AvatarFileId.Should().NotBeNull();
        result.Should().NotContainKey(otherTenant.Id);
        result.Should().NotContainKey(otherEntity.Id);
    }

    private static Employee NewEmployee(Guid tenantId, Guid legalEntityId, string number, string first, string last, Guid? avatar) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        LegalEntityId = legalEntityId,
        EmployeeNumber = number,
        FirstName = first,
        LastName = last,
        Email = $"{number}@example.test",
        AvatarFileId = avatar,
        HireDate = new(2026, 1, 1)
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(currentUser.Object, dateTime.Object),
            new SoftDeleteInterceptor(dateTime.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
