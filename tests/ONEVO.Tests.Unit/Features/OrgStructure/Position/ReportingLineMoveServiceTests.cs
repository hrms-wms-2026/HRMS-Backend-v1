using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.OrgStructure;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class ReportingLineMoveServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    [Fact]
    public async Task MoveAsync_SelectedLeadAndTeam_ChangesOnlyLeadManager()
    {
        await using var db = BuildInMemoryDb();
        var headPosition = AddPosition(db, "Head", null);
        var leadPosition = AddPosition(db, "Team Lead", headPosition);
        var memberPosition = AddPosition(db, "Engineer", leadPosition);
        var alternateLeadPosition = AddPosition(db, "Alternate Lead", headPosition);
        var head = AddHolder(db, "Sarah", "Green", headPosition);
        var lead = AddHolder(db, "Amira", "Khan", leadPosition);
        var firstMember = AddHolder(db, "Nina", "Shah", memberPosition, lead.Id);
        var secondMember = AddHolder(db, "Owen", "Lee", memberPosition, lead.Id);
        var newManager = AddHolder(db, "David", "Stone", alternateLeadPosition, head.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = BuildService(db);
        var result = await service.MoveAsync(
            _tenantId,
            _legalEntityId,
            [lead.Id, firstMember.Id, secondMember.Id],
            newManager.Id,
            "Team realignment");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value!.DirectRelationshipsChanged);
        Assert.Equal(3, result.Value.TotalEmployeesAffected);
        Assert.Equal(2, result.Value.PreservedRelationships);

        var assignments = await db.PositionAssignments.AsNoTracking().ToDictionaryAsync(a => a.EmployeeId);
        Assert.Equal(newManager.Id, assignments[lead.Id].ReportsToEmployeeId);
        Assert.Equal(lead.Id, assignments[firstMember.Id].ReportsToEmployeeId);
        Assert.Equal(lead.Id, assignments[secondMember.Id].ReportsToEmployeeId);

        var history = Assert.Single(await db.ReportingManagerChanges.AsNoTracking().ToListAsync());
        Assert.Equal(lead.Id, history.EmployeeId);
        Assert.Equal(head.Id, history.PreviousManagerEmployeeId);
        Assert.Equal(newManager.Id, history.NewManagerEmployeeId);
        Assert.Equal("Team realignment", history.ChangeReason);

        var notifications = await db.Notifications.AsNoTracking().ToListAsync();
        Assert.Equal(3, notifications.Count);
        Assert.Contains(notifications, notification => notification.RecipientUserId == lead.UserId);
        Assert.Contains(notifications, notification => notification.RecipientUserId == head.UserId);
        Assert.Contains(notifications, notification => notification.RecipientUserId == newManager.UserId);
        Assert.DoesNotContain(notifications, notification => notification.RecipientUserId == firstMember.UserId);
        Assert.DoesNotContain(notifications, notification => notification.RecipientUserId == secondMember.UserId);
    }

    [Fact]
    public async Task PreviewAsync_NewManagerIsOneOfTheMovedPeople_RejectsCycle()
    {
        await using var db = BuildInMemoryDb();
        var leadPosition = AddPosition(db, "Team Lead", null);
        var memberPosition = AddPosition(db, "Engineer", leadPosition);
        var lead = AddHolder(db, "Amira", "Khan", leadPosition);
        var member = AddHolder(db, "Nina", "Shah", memberPosition, lead.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await BuildService(db).PreviewAsync(
            _tenantId,
            _legalEntityId,
            [lead.Id, member.Id],
            member.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        Assert.Contains("cycle", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewAsync_UncheckedPersonWithNobodyLeftAbove_IsRejected()
    {
        await using var db = BuildInMemoryDb();
        var leadPosition = AddPosition(db, "Team Lead", null);
        var memberPosition = AddPosition(db, "Engineer", leadPosition);
        var otherPosition = AddPosition(db, "Director", null);
        var lead = AddHolder(db, "Amira", "Khan", leadPosition);
        var member = AddHolder(db, "Nina", "Shah", memberPosition, lead.Id);
        var director = AddHolder(db, "David", "Stone", otherPosition);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // The lead is the top of their line, so an unchecked report would have no one to stay with.
        var result = await BuildService(db).PreviewAsync(_tenantId, _legalEntityId, [lead.Id], director.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        Assert.Contains("Nina Shah", result.Error!);
    }

    [Fact]
    public async Task MoveAsync_UncheckedReport_StaysWithMoversOldManager_AndEachNewManagerIsNotified()
    {
        await using var db = BuildInMemoryDb();
        var headPosition = AddPosition(db, "Head", null);
        var leadPosition = AddPosition(db, "Team Lead", headPosition);
        var memberPosition = AddPosition(db, "Engineer", leadPosition);
        var targetPosition = AddPosition(db, "Director", headPosition);
        var head = AddHolder(db, "Sarah", "Green", headPosition);
        var lead = AddHolder(db, "Amira", "Khan", leadPosition, head.Id);
        var moving = AddHolder(db, "Nina", "Shah", memberPosition, lead.Id);
        var staying = AddHolder(db, "Owen", "Lee", memberPosition, lead.Id);
        var newManager = AddHolder(db, "David", "Stone", targetPosition, head.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await BuildService(db).MoveAsync(
            _tenantId, _legalEntityId, [lead.Id, moving.Id], newManager.Id, null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.DirectRelationshipsChanged);
        Assert.Equal(1, result.Value.PreservedRelationships);
        Assert.Equal(3, result.Value.TotalEmployeesAffected);
        var stayChange = Assert.Single(result.Value.Changes, c => c.Employee.EmployeeId == staying.Id);
        Assert.Equal(lead.Id, stayChange.CurrentManager!.EmployeeId);
        Assert.Equal(head.Id, stayChange.NewManager.EmployeeId);

        var assignments = await db.PositionAssignments.AsNoTracking().ToDictionaryAsync(a => a.EmployeeId);
        Assert.Equal(newManager.Id, assignments[lead.Id].ReportsToEmployeeId);
        Assert.Equal(lead.Id, assignments[moving.Id].ReportsToEmployeeId);
        Assert.Equal(head.Id, assignments[staying.Id].ReportsToEmployeeId);

        var notifications = await db.Notifications.AsNoTracking().ToListAsync();
        var toNewManager = Assert.Single(notifications, n => n.RecipientUserId == newManager.UserId);
        Assert.Contains("Amira Khan now reports directly to you", toNewManager.Body);
        Assert.DoesNotContain("Owen", toNewManager.Body);
        var toHead = Assert.Single(notifications, n => n.RecipientUserId == head.UserId);
        Assert.Contains("Owen Lee now reports directly to you", toHead.Body);
    }

    [Fact]
    public async Task MoveAsync_SiblingSelections_ChangesEachSelectedRelationship()
    {
        await using var db = BuildInMemoryDb();
        var headPosition = AddPosition(db, "Head", null);
        var leadPosition = AddPosition(db, "Team Lead", headPosition);
        var memberPosition = AddPosition(db, "Engineer", leadPosition);
        var alternateLeadPosition = AddPosition(db, "Alternate Lead", headPosition);
        var head = AddHolder(db, "Sarah", "Green", headPosition);
        var lead = AddHolder(db, "Amira", "Khan", leadPosition, head.Id);
        var firstMember = AddHolder(db, "Nina", "Shah", memberPosition, lead.Id);
        var secondMember = AddHolder(db, "Owen", "Lee", memberPosition, lead.Id);
        var newManager = AddHolder(db, "David", "Stone", alternateLeadPosition, head.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await BuildService(db).MoveAsync(
            _tenantId,
            _legalEntityId,
            [firstMember.Id, secondMember.Id],
            newManager.Id,
            null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.DirectRelationshipsChanged);
        var assignments = await db.PositionAssignments.AsNoTracking().ToDictionaryAsync(a => a.EmployeeId);
        Assert.Equal(newManager.Id, assignments[firstMember.Id].ReportsToEmployeeId);
        Assert.Equal(newManager.Id, assignments[secondMember.Id].ReportsToEmployeeId);
    }

    [Fact]
    public async Task MoveAsync_CheckedGrandchildUnderUncheckedChild_FollowsNearestCheckedAncestor()
    {
        await using var db = BuildInMemoryDb();
        var headPosition = AddPosition(db, "Head", null);
        var leadPosition = AddPosition(db, "Lead", headPosition);
        var supervisorPosition = AddPosition(db, "Supervisor", leadPosition);
        var memberPosition = AddPosition(db, "Engineer", supervisorPosition);
        var targetPosition = AddPosition(db, "Director", headPosition);
        var head = AddHolder(db, "Sarah", "Green", headPosition);
        var lead = AddHolder(db, "Amira", "Khan", leadPosition, head.Id);
        var supervisor = AddHolder(db, "Nina", "Shah", supervisorPosition, lead.Id);
        var member = AddHolder(db, "Owen", "Lee", memberPosition, supervisor.Id);
        var newManager = AddHolder(db, "David", "Stone", targetPosition, head.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await BuildService(db).MoveAsync(
            _tenantId,
            _legalEntityId,
            [lead.Id, member.Id],
            newManager.Id,
            null);

        // Only the checked people move: the unchecked supervisor stays (with the lead's old
        // manager) and the checked member goes with the lead, its nearest checked ancestor.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, result.Value!.DirectRelationshipsChanged);
        var assignments = await db.PositionAssignments.AsNoTracking().ToDictionaryAsync(a => a.EmployeeId);
        Assert.Equal(newManager.Id, assignments[lead.Id].ReportsToEmployeeId);
        Assert.Equal(head.Id, assignments[supervisor.Id].ReportsToEmployeeId);
        Assert.Equal(lead.Id, assignments[member.Id].ReportsToEmployeeId);
    }

    private ReportingLineMoveService BuildService(ApplicationDbContext db)
    {
        var hierarchy = new Mock<IEmployeeHierarchyClosureRepository>();
        hierarchy.Setup(repository => repository.RebuildAsync(_tenantId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(provider => provider.UtcNow).Returns(DateTimeOffset.Parse("2026-10-14T08:00:00Z"));
        return new ReportingLineMoveService(db, new PositionPeopleChartReader(db), hierarchy.Object, clock.Object);
    }

    private Guid AddPosition(ApplicationDbContext db, string name, Guid? reportsTo)
    {
        var id = Guid.NewGuid();
        db.Positions.Add(new PositionEntity
        {
            Id = id,
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            Name = name,
            ReportsToPositionId = reportsTo,
            IsActive = true,
            MaxOccupancy = 10
        });
        return id;
    }

    private EmployeeEntity AddHolder(
        ApplicationDbContext db,
        string first,
        string last,
        Guid positionId,
        Guid? reportsTo = null)
    {
        var employee = new EmployeeEntity
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            FirstName = first,
            LastName = last,
            Email = $"{first}@test.dev",
            EmployeeNumber = first
        };
        db.Employees.Add(employee);
        db.PositionAssignments.Add(new PositionAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            EmployeeId = employee.Id,
            PositionId = positionId,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = PositionAssignmentStatus.Active,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            ReportsToEmployeeId = reportsTo
        });
        return employee;
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
