using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Revert;
using ONEVO.Domain.Features.Inbox.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

/// <summary>The Inbox Standard parts of the router: kinds, generic revert, history, bulk, merged steps.</summary>
public partial class InboxRouterTests
{
    private static InboxKindDescriptor Kind(string source, string kind, InboxCapabilities caps)
        => new(source, kind, kind, InboxCategories.Request, caps);

    private static InboxItem LeaveItem(Guid id, string title = "Leave") => new(
        "leave", "request", id, InboxCategories.Request, "annual", title, null, "approved", InboxDirections.Received, null,
        "legal_entity", null, null, null, null, T0, T0, [], 0, false, new Dictionary<string, IReadOnlyList<string>>());

    private static InboxItemDetail Detail(InboxItem item, params InboxStep[] steps)
        => new(item, steps, null, [], null, null, null, null);

    [Fact]
    public void Kinds_lists_every_registered_descriptor()
    {
        _kinds.Add(Kind("leave", "request", InboxCapabilities.Bulk));

        Router().GetKinds().Should().ContainSingle().Which.Kind.Should().Be("request");
    }

    [Fact]
    public async Task Revert_goes_to_the_kinds_reverter_when_its_descriptor_declares_Revert()
    {
        _kinds.Add(Kind("leave", "request", InboxCapabilities.Revert));
        var reverter = new Mock<IInboxDecisionReverter>();
        reverter.SetupGet(r => r.Source).Returns("leave");
        reverter.SetupGet(r => r.Kind).Returns("request");
        var id = Guid.NewGuid();
        reverter.Setup(r => r.RevertAsync(TenantId, new ItemRef("leave", "request", id), Me, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<InboxActionResult>.Success(new InboxActionResult([Other])));
        _reverters.Add(reverter.Object);

        var result = await Router().ExecuteActionAsync(TenantId, Me, "leave", "request", id, "revert", null, default);

        result.IsSuccess.Should().BeTrue();
        _leave.Actions.Should().BeEmpty();
        _realtime.Verify(r => r.InboxChangedAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(Other)),
            new ItemRef("leave", "request", id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revert_stays_with_the_provider_for_kinds_without_a_Revert_descriptor()
    {
        var id = Guid.NewGuid();

        await Router().ExecuteActionAsync(TenantId, Me, "work", "approval", id, "revert", null, default);

        _work.Actions.Should().ContainSingle().Which.Action.Should().Be("revert");
    }

    [Fact]
    public async Task The_list_offers_Undo_with_its_deadline_only_on_the_callers_latest_revertable_decision()
    {
        _kinds.Add(Kind("leave", "request", InboxCapabilities.Revert));
        var reverter = new Mock<IInboxDecisionReverter>();
        reverter.SetupGet(r => r.Source).Returns("leave");
        reverter.SetupGet(r => r.Kind).Returns("request");
        _reverters.Add(reverter.Object);
        var mine = Guid.NewGuid();
        var overtaken = Guid.NewGuid();
        _leave.Items.AddRange([LeaveItem(mine, "mine"), LeaveItem(overtaken, "overtaken")]);
        var now = T0.AddHours(1);
        var decision = _journal.Add(TenantId, new ItemRef("leave", "request", mine), InboxJournalActions.Approve, Me, now.AddMinutes(-10));
        _journal.Add(TenantId, new ItemRef("leave", "request", overtaken), InboxJournalActions.Reject, Me, now.AddMinutes(-10));
        _journal.Add(TenantId, new ItemRef("leave", "request", overtaken), InboxJournalActions.Approve, Other, now.AddMinutes(-5));

        var page = await Router().ListAsync(TenantId, Me, new InboxQuery(), default);

        var undo = page.Items.Single(i => i.Id == mine).AvailableActions.Should().ContainSingle(a => a.Key == "revert").Subject;
        undo.Meta![InboxStandardActions.MetaRevertableUntil].Should().Be(decision.DecidedAt.AddMinutes(30).ToString("O"));
        page.Items.Single(i => i.Id == overtaken).AvailableActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Detail_steps_come_from_the_steps_builder()
    {
        var id = Guid.NewGuid();
        var merged = new List<InboxStep> { new("journal:x", "Approved", null, T0, InboxStepStates.Done, null) };
        _leave.Details[id] = Detail(LeaveItem(id), new InboxStep("submitted", "Submitted", null, T0, InboxStepStates.Done, null));
        _steps.Setup(s => s.BuildAsync(TenantId, new ItemRef("leave", "request", id), It.IsAny<IReadOnlyList<InboxStep>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(merged);

        var detail = await Router().GetDetailAsync(TenantId, Me, "leave", "request", id, default);

        detail.Value!.Steps.Should().BeSameAs(merged);
    }

    [Fact]
    public async Task History_pages_the_callers_decisions_newest_first_with_a_journal_title_for_items_no_longer_visible()
    {
        var visible = Guid.NewGuid();
        _leave.Details[visible] = Detail(LeaveItem(visible, "Annual leave - Bala"));
        var now = T0.AddHours(1);
        _journal.Add(TenantId, new ItemRef("leave", "request", visible), InboxJournalActions.Approve, Me, now.AddMinutes(-50));
        _journal.Add(TenantId, new ItemRef("leave", "request", Guid.NewGuid()), InboxJournalActions.Reject, Me, now.AddMinutes(-40),
            afterJson: """{"title":"Sick leave - Chitra"}""");
        _journal.Add(TenantId, new ItemRef("leave", "request", Guid.NewGuid()), InboxJournalActions.Approve, Me, now.AddMinutes(-30));
        _journal.Add(TenantId, new ItemRef("leave", "request", Guid.NewGuid()), InboxJournalActions.Approve, Other, now.AddMinutes(-20));

        var first = await Router().GetHistoryAsync(TenantId, Me, new InboxHistoryQuery(PageSize: 2), default);
        var second = await Router().GetHistoryAsync(TenantId, Me, new InboxHistoryQuery(Page: 2, PageSize: 2), default);

        first.Total.Should().Be(3);
        first.Items.Select(i => i.Title).Should().Equal("Item no longer available", "Sick leave - Chitra");
        first.Items[1].ActionLabel.Should().Be("Rejected");
        second.Items.Should().ContainSingle().Which.Title.Should().Be("Annual leave - Bala");
        second.Items[0].Item.Should().NotBeNull();
    }

    [Fact]
    public async Task Bulk_runs_each_item_on_its_own_and_reports_partial_failures()
    {
        _kinds.Add(Kind("leave", "request", InboxCapabilities.Bulk));
        var ok = Guid.NewGuid();
        var bad = Guid.NewGuid();
        _leave.ActionResults[bad] = Result<InboxActionResult>.UnprocessableEntity("Balance too low.");

        var result = await Router().ExecuteBulkAsync(TenantId, Me, new InboxBulkRequest("approve",
            [new ItemRef("leave", "request", ok), new ItemRef("leave", "request", bad)], "fine"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Succeeded.Should().Equal(new ItemRef("leave", "request", ok));
        result.Value.Failed.Should().ContainSingle().Which.Message.Should().Be("Balance too low.");
        _leave.Actions.Select(a => a.Body!.Value.GetProperty("comment").GetString()).Should().AllBe("fine");
        _uow.Verify(u => u.ClearTracking(), Times.Exactly(2));
    }

    [Fact]
    public async Task Bulk_is_refused_when_any_item_kind_does_not_declare_Bulk()
    {
        _kinds.Add(Kind("leave", "request", InboxCapabilities.Bulk));
        _kinds.Add(Kind("work", "approval", InboxCapabilities.Chat));

        var result = await Router().ExecuteBulkAsync(TenantId, Me, new InboxBulkRequest("approve",
            [new ItemRef("leave", "request", Guid.NewGuid()), new ItemRef("work", "approval", Guid.NewGuid())]), default);

        result.StatusCode.Should().Be(422);
        _leave.Actions.Should().BeEmpty();
        _work.Actions.Should().BeEmpty();
    }
}
