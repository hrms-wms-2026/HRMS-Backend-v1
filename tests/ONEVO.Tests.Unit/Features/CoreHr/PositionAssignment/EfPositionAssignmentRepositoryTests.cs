using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.PositionAssignment;

public sealed class EfPositionAssignmentRepositoryTests
{
    [Fact]
    public async Task GetActivePrimaryAsync_ReturnsNull_WhenNoActivePrimaryAssignmentExists()
    {
        await using var db = BuildInMemoryDb();
        var repository = CreateRepository(db);

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

        var repository = CreateRepository(db);
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

        var repository = CreateRepository(db);
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

        var repository = CreateRepository(db);
        var count = await repository.CountActiveAsync(tenantId, positionId, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CountActiveAsync_ExcludesActiveAdditionalAuthorityAssignments()
    {
        // Capacity enforcement (FinalizeOnboardingDraftCommandHandler/ApproveAccessGrantRequestCommandHandler)
        // must not count an AdditionalAuthority holder against max_occupancy - only a primary
        // employment assignment consumes a seat.
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        db.PositionAssignments.Add(
            CreateAssignment(tenantId, Guid.NewGuid(), positionId, PositionAssignmentKind.AdditionalAuthority, PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = CreateRepository(db);
        var count = await repository.CountActiveAsync(tenantId, positionId, CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CountActiveAsync_MatchesGetOccupancyPreviewsAsync_AssignedCount_ForTheSamePosition()
    {
        // Regression guard for this whole feature: assignedCount (from the occupant preview) and
        // capacity enforcement (CountActiveAsync) must always agree, since the frontend computes
        // available seats as maxOccupancy - assignedCount. Seeds one primary (counts), one ended
        // primary (excluded by status), and one additional-authority (excluded by kind) so this
        // would fail if either method's filter drifted from the other.
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var primaryEmployee = CreateEmployee(tenantId, "Primary", "Holder", null);
        db.Employees.Add(primaryEmployee);
        db.PositionAssignments.AddRange(
            CreateAssignment(tenantId, primaryEmployee.Id, positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active),
            CreateAssignment(tenantId, Guid.NewGuid(), positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Ended),
            CreateAssignment(tenantId, Guid.NewGuid(), positionId, PositionAssignmentKind.AdditionalAuthority, PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = CreateRepository(db);
        var capacityCount = await repository.CountActiveAsync(tenantId, positionId, CancellationToken.None);
        var previews = await repository.GetOccupancyPreviewsAsync(tenantId, [positionId], 4, CancellationToken.None);

        Assert.Equal(1, capacityCount);
        Assert.Equal(capacityCount, previews[positionId].AssignedCount);
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

        var repository = CreateRepository(db);
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

        var repository = CreateRepository(db);
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

        var repository = CreateRepository(db);
        var result = await repository.GetTrackedAsync(Guid.NewGuid(), assignment.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOccupancyPreviewsAsync_ReturnsEmptyDictionary_WhenNoPositionIdsRequested()
    {
        await using var db = BuildInMemoryDb();
        var repository = CreateRepository(db);

        var result = await repository.GetOccupancyPreviewsAsync(
            Guid.NewGuid(), Array.Empty<Guid>(), 4, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOccupancyPreviewsAsync_OmitsPosition_WhenItHasNoActiveAssignments()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        var repository = CreateRepository(db);
        var result = await repository.GetOccupancyPreviewsAsync(tenantId, [positionId], 4, CancellationToken.None);

        Assert.False(result.ContainsKey(positionId));
    }

    [Fact]
    public async Task GetOccupancyPreviewsAsync_ReturnsDisplayFieldsForOneActiveAssignment()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var avatarFileId = Guid.NewGuid();
        var employee = CreateEmployee(tenantId, "Jane", "Smith", avatarFileId);
        db.Employees.Add(employee);
        db.PositionAssignments.Add(CreateAssignment(
            tenantId, employee.Id, positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = CreateRepository(db);
        var result = await repository.GetOccupancyPreviewsAsync(tenantId, [positionId], 4, CancellationToken.None);

        var preview = result[positionId];
        Assert.Equal(1, preview.AssignedCount);
        var item = Assert.Single(preview.OccupantPreview);
        Assert.Equal(employee.Id, item.EmployeeId);
        Assert.Equal("Jane", item.FirstName);
        Assert.Equal("Smith", item.LastName);
        Assert.Equal(avatarFileId, item.AvatarFileId);
    }

    [Fact]
    public async Task GetOccupancyPreviewsAsync_ExcludesEndedAssignmentsAndAdditionalAuthorityAssignments()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        var endedEmployee = CreateEmployee(tenantId, "Ended", "Person", null);
        var additionalAuthorityEmployee = CreateEmployee(tenantId, "Extra", "Authority", null);
        var activeEmployee = CreateEmployee(tenantId, "Active", "Primary", null);
        db.Employees.AddRange(endedEmployee, additionalAuthorityEmployee, activeEmployee);
        db.PositionAssignments.AddRange(
            CreateAssignment(tenantId, endedEmployee.Id, positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Ended),
            CreateAssignment(tenantId, additionalAuthorityEmployee.Id, positionId, PositionAssignmentKind.AdditionalAuthority, PositionAssignmentStatus.Active),
            CreateAssignment(tenantId, activeEmployee.Id, positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = CreateRepository(db);
        var result = await repository.GetOccupancyPreviewsAsync(tenantId, [positionId], 4, CancellationToken.None);

        var preview = result[positionId];
        Assert.Equal(1, preview.AssignedCount);
        Assert.Equal(activeEmployee.Id, Assert.Single(preview.OccupantPreview).EmployeeId);
    }

    [Fact]
    public async Task GetOccupancyPreviewsAsync_CapsOccupantPreviewAtPreviewLimit_ButKeepsFullAssignedCount()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        var employees = Enumerable.Range(0, 6).Select(i => CreateEmployee(tenantId, $"First{i}", $"Last{i}", null)).ToList();
        db.Employees.AddRange(employees);
        db.PositionAssignments.AddRange(employees.Select(e =>
            CreateAssignment(tenantId, e.Id, positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = CreateRepository(db);
        var result = await repository.GetOccupancyPreviewsAsync(tenantId, [positionId], 4, CancellationToken.None);

        var preview = result[positionId];
        Assert.Equal(6, preview.AssignedCount);
        Assert.Equal(4, preview.OccupantPreview.Count);
    }

    [Fact]
    public async Task GetOccupancyPreviewsAsync_BatchesMultiplePositionsInOneCall_AndGroupsPerPosition()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var positionOneId = Guid.NewGuid();
        var positionTwoId = Guid.NewGuid();
        var employeeOne = CreateEmployee(tenantId, "One", "Person", null);
        var employeeTwo = CreateEmployee(tenantId, "Two", "Person", null);
        db.Employees.AddRange(employeeOne, employeeTwo);
        db.PositionAssignments.AddRange(
            CreateAssignment(tenantId, employeeOne.Id, positionOneId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active),
            CreateAssignment(tenantId, employeeTwo.Id, positionTwoId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = CreateRepository(db);
        var result = await repository.GetOccupancyPreviewsAsync(
            tenantId, [positionOneId, positionTwoId], 4, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(employeeOne.Id, Assert.Single(result[positionOneId].OccupantPreview).EmployeeId);
        Assert.Equal(employeeTwo.Id, Assert.Single(result[positionTwoId].OccupantPreview).EmployeeId);
    }

    [Fact]
    public async Task GetOccupancyPreviewsAsync_DoesNotLeakAnotherTenantsAssignments()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var otherTenantEmployee = CreateEmployee(otherTenantId, "Other", "Tenant", null);
        db.Employees.Add(otherTenantEmployee);
        db.PositionAssignments.Add(CreateAssignment(
            otherTenantId, otherTenantEmployee.Id, positionId, PositionAssignmentKind.PrimaryEmployment, PositionAssignmentStatus.Active));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = CreateRepository(db);
        var result = await repository.GetOccupancyPreviewsAsync(tenantId, [positionId], 4, CancellationToken.None);

        Assert.False(result.ContainsKey(positionId));
    }

    private static EmployeeEntity CreateEmployee(Guid tenantId, string firstName, string lastName, Guid? avatarFileId)
    {
        return new EmployeeEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FirstName = firstName,
            LastName = lastName,
            AvatarFileId = avatarFileId,
        };
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

    private static EfPositionAssignmentRepository CreateRepository(ApplicationDbContext db)
    {
        var clock = Mock.Of<IDateTimeProvider>(p => p.UtcNow == DateTimeOffset.UtcNow);
        var closureRepo = new EfEmployeeHierarchyClosureRepository(db, clock);
        return new EfPositionAssignmentRepository(db, closureRepo);
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
