using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.OrgStructure;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class PositionPeopleChartReaderTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    [Fact]
    public async Task BuildAsync_GivesEveryHolderTheirOwnCard_UnderWhoTheyActuallyReportTo()
    {
        await using var db = BuildInMemoryDb();
        // Head (1 holder) -> Team Lead (2 holders: Amira, Ben) -> Engineer (3 people), plus a
        // vacant Designer position under Team Lead, and a separate Sales Lead branch under Head.
        var headPos = AddPosition(db, "Head", null);
        var leadPos = AddPosition(db, "Team Lead", headPos);
        var engPos = AddPosition(db, "Engineer", leadPos);
        var designerPos = AddPosition(db, "Designer", leadPos);
        var salesPos = AddPosition(db, "Sales Lead", headPos);

        var head = AddHolder(db, "Hana", "Head", headPos);
        var amira = AddHolder(db, "Amira", "Khan", leadPos);
        var ben = AddHolder(db, "Ben", "Lee", leadPos);
        var sam = AddHolder(db, "Sam", "Sales", salesPos);
        var picked = AddHolder(db, "Pia", "Picked", engPos, reportsTo: ben.Id);      // explicit pick in the line
        var unpicked = AddHolder(db, "Uma", "Unpicked", engPos);                       // nobody picked
        var oneOff = AddHolder(db, "Ola", "OneOff", engPos, reportsTo: sam.Id);        // one-off from another line
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var nodes = await new PositionPeopleChartReader(db).BuildAsync(_tenantId, _legalEntityId);
        var byId = nodes.ToDictionary(n => n.Id);

        Assert.Null(byId[$"e:{head.Id}"].ParentId);
        Assert.Equal($"e:{head.Id}", byId[$"e:{amira.Id}"].ParentId);
        Assert.Equal($"e:{head.Id}", byId[$"e:{ben.Id}"].ParentId);

        Assert.Equal(($"e:{ben.Id}", "position"), (byId[$"e:{picked.Id}"].ParentId, byId[$"e:{picked.Id}"].ReportingLine));
        // No pick under a 2-holder position: shown under the first holder (Amira), marked default.
        Assert.Equal(($"e:{amira.Id}", "default"), (byId[$"e:{unpicked.Id}"].ParentId, byId[$"e:{unpicked.Id}"].ReportingLine));
        Assert.Equal(($"e:{sam.Id}", "custom"), (byId[$"e:{oneOff.Id}"].ParentId, byId[$"e:{oneOff.Id}"].ReportingLine));

        var designer = byId[$"p:{designerPos}"];
        Assert.Equal("vacant", designer.Kind);
        Assert.Equal($"e:{amira.Id}", designer.ParentId);
        Assert.Equal(3, byId[$"e:{oneOff.Id}"].HolderCount);
    }

    [Fact]
    public async Task BuildAsync_ShowsInvitedEmployees_ButNeverAsAnyonesManagerOrDefaultHolder()
    {
        await using var db = BuildInMemoryDb();
        var headPos = AddPosition(db, "Head", null);
        var leadPos = AddPosition(db, "Team Lead", headPos);
        var engPos = AddPosition(db, "Engineer", leadPos);
        var head = AddHolder(db, "Hana", "Head", headPos);
        // Invited lead (planned seat, alphabetically first) and an active lead.
        var invitedLead = AddHolder(db, "Aaron", "Invited", leadPos, status: PositionAssignmentStatus.Planned);
        var activeLead = AddHolder(db, "Zed", "Active", leadPos);
        var engineer = AddHolder(db, "Eve", "Eng", engPos);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var nodes = await new PositionPeopleChartReader(db).BuildAsync(_tenantId, _legalEntityId);
        var byId = nodes.ToDictionary(n => n.Id);

        var invitedCard = byId[$"i:{invitedLead.Id}"];
        Assert.True(invitedCard.IsPending);
        Assert.Equal($"e:{head.Id}", invitedCard.ParentId);
        // The only *active* lead is the engineer's manager - the invited one isn't counted.
        Assert.Equal(($"e:{activeLead.Id}", "position"), (byId[$"e:{engineer.Id}"].ParentId, byId[$"e:{engineer.Id}"].ReportingLine));
        Assert.DoesNotContain(nodes, n => n.ParentId == $"i:{invitedLead.Id}");
    }

    [Fact]
    public async Task BuildAsync_LiftsCardsCaughtInAOneOffCycleToTheTop()
    {
        await using var db = BuildInMemoryDb();
        var posA = AddPosition(db, "A", null);
        var posB = AddPosition(db, "B", null);
        var a = AddHolder(db, "Ann", "A", posA);
        var b = AddHolder(db, "Bob", "B", posB);
        await db.SaveChangesAsync();
        // A and B each picked the other as their one-off manager.
        var assignments = await db.PositionAssignments.ToListAsync();
        assignments.Single(x => x.EmployeeId == a.Id).ReportsToEmployeeId = b.Id;
        assignments.Single(x => x.EmployeeId == b.Id).ReportsToEmployeeId = a.Id;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var nodes = await new PositionPeopleChartReader(db).BuildAsync(_tenantId, _legalEntityId);

        Assert.All(nodes, n => Assert.Null(n.ParentId));
    }

    private Guid AddPosition(ApplicationDbContext db, string name, Guid? reportsTo)
    {
        var id = Guid.NewGuid();
        db.Positions.Add(new PositionEntity
        {
            Id = id, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = name,
            ReportsToPositionId = reportsTo, IsActive = true, MaxOccupancy = 5
        });
        return id;
    }

    private EmployeeEntity AddHolder(
        ApplicationDbContext db, string first, string last, Guid positionId, Guid? reportsTo = null,
        string status = PositionAssignmentStatus.Active)
    {
        var employee = new EmployeeEntity
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId,
            FirstName = first, LastName = last, Email = $"{first}@test.dev", EmployeeNumber = first
        };
        db.Employees.Add(employee);
        db.PositionAssignments.Add(new PositionAssignment
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = employee.Id, PositionId = positionId,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = status,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            ReportsToEmployeeId = reportsTo,
        });
        return employee;
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            optionsBuilder.Options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
