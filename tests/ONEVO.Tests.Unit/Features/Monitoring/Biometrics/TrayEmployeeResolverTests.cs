using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Services.Monitoring.Biometrics;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public sealed class TrayEmployeeResolverTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid UserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid EmployeeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task ResolveAsync_ReturnsCoreHrIdentityForMatchingTenantAndUser()
    {
        await using var db = BuildDb();
        db.Employees.Add(new Employee
        {
            Id = EmployeeId,
            TenantId = TenantId,
            UserId = UserId,
            EmployeeNumber = "ONEVO1234",
            FirstName = "Alex",
            LastName = "Smith",
            Email = "alex@onevo.test",
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEmployeeRepository(db);
        var sut = new TrayEmployeeResolver(repository);

        var result = await sut.ResolveAsync(TenantId, UserId, default);

        result.Should().Be(new EmployeeIdentity(EmployeeId, "ONEVO1234", "Alex Smith", "alex@onevo.test"));
    }

    [Fact]
    public async Task ResolveAsync_DoesNotReturnAnotherTenantsEmployee()
    {
        await using var db = BuildDb();
        db.Employees.Add(new Employee
        {
            Id = EmployeeId,
            TenantId = TenantId,
            UserId = UserId,
            EmployeeNumber = "ONEVO1234",
            FirstName = "Alex",
            LastName = "Smith",
            Email = "alex@onevo.test",
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEmployeeRepository(db);
        var sut = new TrayEmployeeResolver(repository);

        var result = await sut.ResolveAsync(OtherTenantId, UserId, default);

        result.Should().BeNull();
    }

    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
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
