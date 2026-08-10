using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

namespace ONEVO.Tests.Unit.Features.CoreHr.PositionAssignment;

public sealed class EfPositionAssignmentRepositoryTests
{
    [Fact]
    public async Task GetActivePrimaryAsync_ReturnsNull_WhenNoActivePrimaryAssignmentExists()
    {
        await using var db = BuildInMemoryDb();
        var repository = new EfPositionAssignmentRepository(db);

        var result = await repository.GetActivePrimaryAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActivePrimaryAsync_IgnoresEndedOrAdditionalAuthorityAssignments()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        db.PositionAssignments.AddRange(
            CreateAssignment(tenantId, employeeId, Guid.NewGuid(), PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Ended),
            CreateAssignment(tenantId, employeeId, Guid.NewGuid(), PositionAssignmentKind.AdditionalAuthority, PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionAssignmentRepository(db);
        var result = await repository.GetActivePrimaryAsync(tenantId, employeeId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActivePrimaryAsync_ReturnsTheActivePrimaryAssignment()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var active = CreateAssignment(tenantId, employeeId, positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active);
        db.PositionAssignments.Add(active);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionAssignmentRepository(db);
        var result = await repository.GetActivePrimaryAsync(tenantId, employeeId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(positionId, result!.PositionId);
    }

    [Fact]
    public async Task CountActiveAsync_CountsOnlyActiveAssignmentsForThatPosition()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        db.PositionAssignments.AddRange(
            CreateAssignment(tenantId, Guid.NewGuid(), positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active),
            CreateAssignment(tenantId, Guid.NewGuid(), positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Ended),
            CreateAssignment(tenantId, Guid.NewGuid(), Guid.NewGuid(), PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionAssignmentRepository(db);
        var count = await repository.CountActiveAsync(tenantId, positionId, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task HasActivePrimaryInLegalEntityAsync_ReturnsFalse_WhenAssignmentIsInADifferentLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();

        db.Positions.Add(new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = positionId,
            TenantId = tenantId,
            LegalEntityId = otherLegalEntityId,
            Name = "Engineer",
        });
        db.PositionAssignments.Add(
            CreateAssignment(tenantId, employeeId, positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionAssignmentRepository(db);
        var result = await repository.HasActivePrimaryInLegalEntityAsync(tenantId, employeeId, legalEntityId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasActivePrimaryInLegalEntityAsync_ReturnsTrue_WhenAssignmentIsInTheGivenLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();

        db.Positions.Add(new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = positionId,
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = "Engineer",
        });
        db.PositionAssignments.Add(
            CreateAssignment(tenantId, employeeId, positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionAssignmentRepository(db);
        var result = await repository.HasActivePrimaryInLegalEntityAsync(tenantId, employeeId, legalEntityId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task GetTrackedAsync_ReturnsNull_ForAnotherTenantsAssignment()
    {
        await using var db = BuildInMemoryDb();
        var assignment = CreateAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active);
        db.PositionAssignments.Add(assignment);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionAssignmentRepository(db);
        var result = await repository.GetTrackedAsync(Guid.NewGuid(), assignment.Id, CancellationToken.None);

        Assert.Null(result);
    }

    private static ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment CreateAssignment(
        Guid tenantId, Guid employeeId, Guid positionId, string kind, string status)
    {
        return new ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            PositionId = positionId,
            AssignmentKind = kind,
            AssignmentStatus = status,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(
            currentUser.Object,
            dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options,
            auditInterceptor,
            softDeleteInterceptor,
            domainEventInterceptor,
            tenantContext.Object);
    }
}
