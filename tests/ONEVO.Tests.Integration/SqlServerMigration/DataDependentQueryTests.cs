using ONEVO.Application.Features.Conversations.Contracts;
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

    private static ConversationMessage Message(MyTeamDb h, Guid conversationId, Guid author) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, ConversationId = conversationId, AuthorEmployeeId = author,
        Kind = ConversationMessageKinds.Text, BodyHtml = "<p>x</p>",
    };
}
