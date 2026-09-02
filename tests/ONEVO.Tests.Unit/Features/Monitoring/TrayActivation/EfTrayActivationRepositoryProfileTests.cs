using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.TrayActivation;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;

public sealed class EfTrayActivationRepositoryProfileTests
{
    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    [Fact]
    public async Task SingleActiveEmployee_ResolvesWithoutLegalEntityContext()
    {
        await using var db = BuildDb();
        var ids = await SeedAsync(db, includeSecondActive: false);
        var repository = new EfTrayActivationRepository(db, new Mock<IDateTimeProvider>().Object);

        var profile = await repository.FindEmployeeProfileAsync(
            ids.UserId, ids.TenantId, null, CancellationToken.None);

        profile.Should().NotBeNull();
        profile!.EmployeeNumber.Should().Be("EMP-A");
    }

    [Fact]
    public async Task MultipleActiveEmployees_WithoutLegalEntityContext_FailsClosed()
    {
        await using var db = BuildDb();
        var ids = await SeedAsync(db, includeSecondActive: true);
        var repository = new EfTrayActivationRepository(db, new Mock<IDateTimeProvider>().Object);

        var profile = await repository.FindEmployeeProfileAsync(
            ids.UserId, ids.TenantId, null, CancellationToken.None);

        profile.Should().BeNull();
    }

    [Fact]
    public async Task MultipleActiveEmployees_WithLegalEntityContext_ResolvesExactEmployee()
    {
        await using var db = BuildDb();
        var ids = await SeedAsync(db, includeSecondActive: true);
        var repository = new EfTrayActivationRepository(db, new Mock<IDateTimeProvider>().Object);

        var profile = await repository.FindEmployeeProfileAsync(
            ids.UserId, ids.TenantId, ids.LegalEntityB, CancellationToken.None);

        profile.Should().NotBeNull();
        profile!.EmployeeNumber.Should().Be("EMP-B");
        profile.FirstName.Should().Be("EMP-B");
    }

    [Fact]
    public async Task LegalEntityFromAnotherTenant_IsRejected()
    {
        await using var db = BuildDb();
        var ids = await SeedAsync(db, includeSecondActive: true);
        var repository = new EfTrayActivationRepository(db, new Mock<IDateTimeProvider>().Object);

        var profile = await repository.FindEmployeeProfileAsync(
            ids.UserId, Guid.NewGuid(), ids.LegalEntityB, CancellationToken.None);

        profile.Should().BeNull();
    }

    [Fact]
    public async Task InactiveEmployee_IsIgnored()
    {
        await using var db = BuildDb();
        var ids = await SeedAsync(db, includeSecondActive: false, includeInactive: true);
        var repository = new EfTrayActivationRepository(db, new Mock<IDateTimeProvider>().Object);

        var profile = await repository.FindEmployeeProfileAsync(
            ids.UserId, ids.TenantId, ids.LegalEntityB, CancellationToken.None);

        profile.Should().BeNull();
    }

    private static async Task<Ids> SeedAsync(
        ApplicationDbContext db, bool includeSecondActive, bool includeInactive = false)
    {
        const int activeStatusId = 1;
        const int inactiveStatusId = 2;
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var legalEntityA = Guid.NewGuid();
        var legalEntityB = Guid.NewGuid();
        db.EmploymentStatuses.AddRange(
            new EmploymentStatus { Id = activeStatusId, Code = "active", Label = "Active" },
            new EmploymentStatus { Id = inactiveStatusId, Code = "inactive", Label = "Inactive" });
        db.Employees.Add(Employee(tenantId, userId, legalEntityA, activeStatusId, "EMP-A"));
        if (includeSecondActive)
            db.Employees.Add(Employee(tenantId, userId, legalEntityB, activeStatusId, "EMP-B"));
        if (includeInactive)
            db.Employees.Add(Employee(tenantId, userId, legalEntityB, inactiveStatusId, "EMP-INACTIVE"));
        await db.SaveChangesAsync();
        return new Ids(tenantId, userId, legalEntityB);
    }

    private static Employee Employee(
        Guid tenantId, Guid userId, Guid legalEntityId, int statusId, string number) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        LegalEntityId = legalEntityId,
        EmploymentStatusId = statusId,
        EmployeeNumber = number,
        FirstName = number,
        LastName = "Employee",
        Email = $"{number.ToLowerInvariant()}@example.com",
        HireDate = new DateOnly(2026, 1, 1)
    };

    private sealed record Ids(Guid TenantId, Guid UserId, Guid LegalEntityB);
}
