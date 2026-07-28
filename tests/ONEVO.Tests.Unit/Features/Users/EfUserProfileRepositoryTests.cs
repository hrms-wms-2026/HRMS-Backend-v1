using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Users;

namespace ONEVO.Tests.Unit.Features.Users;

public sealed class EfUserProfileRepositoryTests
{
    [Fact]
    public async Task GetActiveReferencePhotoAsync_ThrowsWhenDuplicateActiveRowsExist()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var tenantContext = CreateTenantContext(tenantId);
        await using var db = CreateContext(tenantContext.Object);

        db.VerificationReferencePhotos.AddRange(
            CreatePhoto(tenantId, employeeId),
            CreatePhoto(tenantId, employeeId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfUserProfileRepository(db, tenantContext.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.GetActiveReferencePhotoAsync(employeeId, CancellationToken.None));
    }

    [Fact]
    public async Task ProfileReadQueries_DoNotTrackReturnedEntities()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var tenantContext = CreateTenantContext(tenantId);
        await using var db = CreateContext(tenantContext.Object);

        db.Employees.Add(new Employee
        {
            Id = employeeId,
            TenantId = tenantId,
            UserId = userId,
            EmployeeNumber = "EMP-001",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.test",
            HireDate = new DateOnly(2026, 1, 1)
        });
        db.RegisteredAgents.Add(new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            DeviceId = "device-001",
            DeviceName = "Ada laptop",
            OsVersion = "Windows 11",
            AgentVersion = "1.0.0"
        });
        db.EmployeeWorkLocationSettings.Add(new EmployeeWorkLocationSettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            SetById = userId
        });
        db.VerificationReferencePhotos.Add(CreatePhoto(tenantId, employeeId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfUserProfileRepository(db, tenantContext.Object);

        Assert.NotNull(await repository.GetEmployeeByUserIdAsync(userId, CancellationToken.None));
        Assert.Single(await repository.GetAgentsByEmployeeIdAsync(employeeId, CancellationToken.None));
        Assert.NotNull(await repository.GetWorkLocationSettingsAsync(employeeId, CancellationToken.None));
        Assert.NotNull(await repository.GetActiveReferencePhotoAsync(employeeId, CancellationToken.None));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static VerificationReferencePhoto CreatePhoto(Guid tenantId, Guid employeeId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            PhotoFileId = Guid.NewGuid(),
            Status = "approved",
            IsActive = true
        };

    private static Mock<ITenantContext> CreateTenantContext(Guid tenantId)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(context => context.TenantId).Returns(tenantId);
        tenantContext.SetupGet(context => context.ContextMode).Returns(TenantContextMode.Tenant);
        tenantContext.SetupGet(context => context.IsResolved).Returns(true);
        return tenantContext;
    }

    private static ApplicationDbContext CreateContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-profile-repository-{Guid.NewGuid()}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }
}
