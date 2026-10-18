using FluentAssertions;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

public class InboxStepsBuilderTests
{
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static InboxDecisionJournalEntry Journal(string action, Guid actor, int minutes, Guid? revertedBy = null) => new()
    {
        Id = Guid.NewGuid(), Action = action, ActorEmployeeId = actor, DecidedAt = T0.AddMinutes(minutes), RevertedByJournalId = revertedBy,
    };

    private static ConversationMessage Event(string body, int minutes) => new()
    {
        Id = Guid.NewGuid(), Kind = ConversationMessageKinds.System, BodyHtml = body, CreatedAt = T0.AddMinutes(minutes),
    };

    [Fact]
    public void Merges_module_steps_journal_and_conversation_events_in_time_order_with_undated_steps_last()
    {
        var module = new List<InboxStep>
        {
            new("submitted", "Submitted", new InboxPerson(Requester, "Bala"), T0, InboxStepStates.Done, null),
            new("final", "Final decision", null, null, InboxStepStates.Upcoming, null),
        };
        var journal = new[] { Journal(InboxJournalActions.Acknowledge, Approver, 10), Journal(InboxJournalActions.Escalate, Approver, 30) };
        var events = new[] { Event("Anu added Chitra &amp; Co", 20) };

        var steps = InboxStepsBuilder.Merge(module, journal, events, new Dictionary<Guid, string> { [Approver] = "Anu" });

        steps.Select(s => s.Label).Should().Equal("Submitted", "Acknowledged", "Anu added Chitra & Co", "Escalated", "Final decision");
        steps[1].Actor!.Name.Should().Be("Anu");
    }

    [Fact]
    public void A_journal_row_matching_a_module_step_is_folded_in_and_a_reverted_one_is_struck_through()
    {
        var module = new List<InboxStep>
        {
            new("approver:1", "Approved by Anu", new InboxPerson(Approver, "Anu"), T0.AddMinutes(5), InboxStepStates.Done, null),
        };
        var revert = Journal(InboxJournalActions.Revert, Approver, 9);
        var journal = new[] { Journal(InboxJournalActions.Approve, Approver, 5, revertedBy: revert.Id), revert };

        var steps = InboxStepsBuilder.Merge(module, journal, [], new Dictionary<Guid, string>());

        steps.Select(s => (s.Label, s.State)).Should().Equal(
            ("Approved by Anu", InboxStepStates.Reverted), ("Decision undone", InboxStepStates.Done));
    }

    [Theory]
    [InlineData("resolve:converted_to_leave", "Resolved - Converted to leave")]
    [InlineData("change_decision", "Change decision")]
    [InlineData("request_info", "Asked for more information")]
    public void Journal_labels(string action, string label) => InboxStepsBuilder.JournalLabel(action).Should().Be(label);
}
