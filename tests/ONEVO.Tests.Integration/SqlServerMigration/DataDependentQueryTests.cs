using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Services.OrgStructure;
using ONEVO.Tests.Integration.Support;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.Conversations;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// Queries the translation sweep can't reach with generated arguments because they only run once
/// earlier lookups find rows, and whose LINQ is unusual enough to be worth running on SQL Server.
/// </summary>
public sealed class DataDependentQueryTests
{
    [Fact]
    public async Task UnreadCounts_JoinParticipantsAndSkipHiddenAndOwnMessages()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var me = h.AddEmployee(db).Id;
        var other = h.AddEmployee(db).Id;
        var subject = new ItemRef("work", "approval", Guid.NewGuid());
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, SubjectSource = subject.Source, SubjectKind = subject.Kind,
            SubjectId = subject.Id, LastMessageAt = DateTimeOffset.UtcNow,
        };
        db.Conversations.Add(conversation);
        db.ConversationParticipants.Add(new ConversationParticipant
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, ConversationId = conversation.Id, EmployeeId = me,
            JoinReason = ConversationJoinReasons.Base, AddedAt = DateTimeOffset.UtcNow.AddHours(-2),
            LastReadAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        var hidden = Message(h, conversation.Id, other);
        db.ConversationMessages.AddRange(Message(h, conversation.Id, other), Message(h, conversation.Id, me), hidden);
        db.ConversationMessageHides.Add(new ConversationMessageHide
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, MessageId = hidden.Id, EmployeeId = me,
        });
        await db.SaveChangesAsync();

        await using var read = h.NewTenantContext();
        var counts = await new EfConversationRepository(read).CountUnreadAsync(h.TenantId, me, [subject]);

        Assert.Equal(1, Assert.Single(counts).Value);
    }

    [Fact]
    public async Task EmployeeList_WithAttendance_ReadsAttendanceLeaveAndBreaks()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var employee = h.AddEmployee(db, h.AddDepartment(db));
        await db.SaveChangesAsync();
        var scope = new EmployeeVisibilityScope(true, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        var filter = new EmployeeListFilter(null, null, null, [employee.Id]);

        var (items, total) = await new EfEmployeeRepository(db).ListVisibleAsync(
            h.TenantId, scope, filter, 1, 10, default, new EmployeeListAttendanceOptions(DateTimeOffset.UtcNow));

        Assert.Equal(1, total);
        Assert.Equal(employee.Id, Assert.Single(items).Id);
    }

    [Fact]
    public async Task EmployeeList_ShowsAPlannedPositionAndTheManagerItReportsTo()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var manager = h.AddEmployee(db, lastName: "Manager");
        var managerPositionId = h.AddPositionHeldBy(db, manager.Id);
        var starter = h.AddEmployee(db);
        var plannedPositionId = h.AddPositionHeldBy(db, starter.Id);
        await db.SaveChangesAsync();
        (await db.PositionAssignments.SingleAsync(a => a.EmployeeId == starter.Id)).AssignmentStatus = PositionAssignmentStatus.Planned;
        (await db.Positions.SingleAsync(p => p.Id == plannedPositionId)).ReportsToPositionId = managerPositionId;
        await db.SaveChangesAsync();
        var scope = new EmployeeVisibilityScope(true, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

        var (items, _) = await new EfEmployeeRepository(db).ListVisibleAsync(
            h.TenantId, scope, new EmployeeListFilter(null, null, null, [starter.Id]), 1, 10);

        var item = Assert.Single(items);
        Assert.True(item.PositionPending);
        Assert.Equal(plannedPositionId, item.PositionId);
        Assert.Equal(manager.Id, item.ReportingManagerId);
    }

    [Fact]
    public async Task ReportingLineMove_ChangesTheManagerAndNotifies()
    {
        var h = await MyTeamDb.CreateAsync();
        await using (var setup = h.NewContext())
        {
            await setup.Database.ExecuteSqlRawAsync(TestDb.DisableForeignKeysSql("notifications"));
        }

        await using var db = h.NewContext();
        var head = h.AddEmployee(db);
        var headPositionId = h.AddPositionHeldBy(db, head.Id);
        var lead = h.AddEmployee(db);
        var leadPositionId = h.AddPositionHeldBy(db, lead.Id);
        var newManager = h.AddEmployee(db);
        var newManagerPositionId = h.AddPositionHeldBy(db, newManager.Id);
        await db.SaveChangesAsync();
        foreach (var id in new[] { leadPositionId, newManagerPositionId })
            (await db.Positions.SingleAsync(p => p.Id == id)).ReportsToPositionId = headPositionId;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var clock = new SystemDateTimeProvider();
        var service = new ReportingLineMoveService(
            db, new PositionPeopleChartReader(db), new EfEmployeeHierarchyClosureRepository(db, clock), clock);

        var result = await service.MoveAsync(h.TenantId, h.LegalEntityId, [lead.Id], newManager.Id, "Realignment");

        Assert.True(result.IsSuccess, result.Error);
        var assignment = await db.PositionAssignments.AsNoTracking().SingleAsync(a => a.EmployeeId == lead.Id);
        Assert.Equal(newManager.Id, assignment.ReportsToEmployeeId);
        Assert.NotEmpty(await db.Notifications.AsNoTracking().ToListAsync());
    }

    private static ConversationMessage Message(MyTeamDb h, Guid conversationId, Guid author) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, ConversationId = conversationId, AuthorEmployeeId = author,
        Kind = ConversationMessageKinds.Text, BodyHtml = "<p>x</p>",
    };
}
