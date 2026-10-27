using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.AcknowledgeException;
using ONEVO.Application.Features.Monitoring.Exceptions.Inbox;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Tests.Unit.Fakes;
using ONEVO.Tests.Unit.Features.Inbox;
using Xunit;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

/// <summary>A reviewer's chat activity on an Open exception case acknowledges it, the same way the Acknowledge button does.</summary>
public class ExceptionCaseActivityAcknowledgerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<ISender> _sender = new();
    private readonly InMemoryInboxDecisionJournal _journal = new();
    private readonly Mock<IInboxEscalationRecorder> _recorder = new();
    private readonly Mock<IInboxRealtimePublisher> _realtime = new();
    private readonly List<MonitoringException> _cases = new();

    public ExceptionCaseActivityAcknowledgerTests()
    {
        _exceptions.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => _cases.FirstOrDefault(c => c.Id == id));
        // The module's command checks the reviewer scope; here it acknowledges like the real one would.
        _sender.Setup(s => s.Send(It.IsAny<AcknowledgeExceptionCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<Result> command, CancellationToken _) =>
            {
                var c = _cases.First(x => x.Id == ((AcknowledgeExceptionCommand)command).ExceptionId);
                c.Status = ExceptionStatus.Acknowledged;
                c.AcknowledgedAt = Now;
                c.AcknowledgedById = Reviewer;
            })
            .ReturnsAsync(Result.Success());
    }

    private MonitoringException Case(ExceptionStatus status = ExceptionStatus.Open, ExceptionType type = ExceptionType.SustainedLowActivity)
    {
        var c = new MonitoringException
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = Subject, Type = type, Status = status,
            Title = "Case", Description = "d", DetectedAt = Now.AddDays(-1),
        };
        _cases.Add(c);
        return c;
    }

    private ExceptionCaseActivityAcknowledger Acknowledger() => new(_exceptions.Object,
        new ExceptionCaseActions(_sender.Object, _exceptions.Object, _journal, _recorder.Object, new InboxEscalationPolicyRegistry([]),
            new FakeUnitOfWork(), new FakeDateTimeProvider { UtcNow = Now }),
        _realtime.Object);

    [Theory]
    [InlineData(ConversationActivities.MessagePosted, "Acknowledged automatically: replied in the chat.")]
    [InlineData(ConversationActivities.MemberAdded, "Acknowledged automatically: added someone to the chat.")]
    [InlineData(ConversationActivities.Reacted, "Acknowledged automatically: reacted in the chat.")]
    public async Task A_reviewers_chat_activity_acknowledges_an_open_case_as_an_undoable_decision(string activity, string note)
    {
        var c = Case();
        var item = new ItemRef("monitoring", "exception_case", c.Id);

        await Acknowledger().OnActivityAsync(TenantId, item, Reviewer, activity, default);

        c.Status.Should().Be(ExceptionStatus.Acknowledged);
        // The same journal row as the Acknowledge button, so the reviewer's usual Undo reopens it.
        var row = _journal.Entries.Should().ContainSingle().Subject;
        row.Action.Should().Be("acknowledge");
        row.ActorEmployeeId.Should().Be(Reviewer);
        row.Comment.Should().Be(note);
        ExceptionStateSnapshot.Parse(row.BeforeJson)!.Status.Should().Be("Open");
        _realtime.Verify(r => r.InboxChangedAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(Reviewer) && ids.Contains(Subject)),
            item, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_calendar_meeting_case_is_acknowledged_the_same_way()
    {
        var c = Case(type: ExceptionType.MeetingNoShow);

        await Acknowledger().OnActivityAsync(TenantId, new ItemRef("calendar", "meeting_exception", c.Id), Reviewer,
            ConversationActivities.MessagePosted, default);

        c.Status.Should().Be(ExceptionStatus.Acknowledged);
        _journal.Entries.Should().ContainSingle().Which.Source.Should().Be("calendar");
    }

    [Theory]
    [InlineData(ExceptionStatus.Acknowledged)]
    [InlineData(ExceptionStatus.Escalated)]
    [InlineData(ExceptionStatus.Resolved)]
    public async Task Only_an_open_case_is_acknowledged(ExceptionStatus status)
    {
        var c = Case(status);

        await Acknowledger().OnActivityAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), Reviewer,
            ConversationActivities.MessagePosted, default);

        c.Status.Should().Be(status);
        _sender.Invocations.Should().BeEmpty();
        _journal.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task The_employee_the_case_is_about_never_acknowledges_it_by_explaining_themselves()
    {
        var c = Case();

        await Acknowledger().OnActivityAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), Subject,
            ConversationActivities.MessagePosted, default);

        c.Status.Should().Be(ExceptionStatus.Open);
        _sender.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Someone_who_cannot_act_on_the_case_is_skipped_quietly()
    {
        var c = Case();
        _sender.Setup(s => s.Send(It.IsAny<AcknowledgeExceptionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.NotFound("Exception not found."));

        await Acknowledger().OnActivityAsync(TenantId, new ItemRef("monitoring", "exception_case", c.Id), Guid.NewGuid(),
            ConversationActivities.MemberAdded, default);

        c.Status.Should().Be(ExceptionStatus.Open);
        _journal.Entries.Should().BeEmpty();
        _realtime.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Other_inbox_items_are_left_alone()
    {
        await Acknowledger().OnActivityAsync(TenantId, new ItemRef("leave", "request", Guid.NewGuid()), Reviewer,
            ConversationActivities.MessagePosted, default);

        _exceptions.Invocations.Should().BeEmpty();
        _sender.Invocations.Should().BeEmpty();
    }
}
