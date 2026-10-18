using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Revert;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

public partial class InboxRouterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid ProjectA = Guid.NewGuid();
    private static readonly Guid ProjectB = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeProvider _work = new("work", "Work");
    private readonly FakeProvider _leave = new("leave", "Leave");
    private readonly Mock<IConversationUnreadCounts> _unread = new();
    private readonly Mock<IInboxRealtimePublisher> _realtime = new();
    private readonly List<InboxKindDescriptor> _kinds = new();
    private readonly InMemoryInboxDecisionJournal _journal = new();
    private readonly List<IInboxDecisionReverter> _reverters = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IInboxStepsBuilder> _steps = new();

    public InboxRouterTests()
    {
        _unread.Setup(u => u.GetAsync(TenantId, Me, It.IsAny<IReadOnlyCollection<ItemRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ItemRef, int>());
        _clock.SetupGet(c => c.UtcNow).Returns(T0.AddHours(1));
        _steps.Setup(s => s.BuildAsync(It.IsAny<Guid>(), It.IsAny<ItemRef>(), It.IsAny<IReadOnlyList<InboxStep>>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, ItemRef _, IReadOnlyList<InboxStep> steps, CancellationToken _) => Task.FromResult(steps));
    }

    private InboxRouter Router() => new(new IInboxSourceProvider[] { _work, _leave }, _unread.Object, _realtime.Object,
        NullLogger<InboxRouter>.Instance, new InboxKindRegistry(_kinds), _journal, _reverters, _steps.Object, _uow.Object, _clock.Object);

    private static InboxItem Item(
        string source, string title, int minutesAfterT0, bool pending = false, string category = InboxCategories.Request,
        string direction = InboxDirections.Received, Guid? project = null, string status = "pending", string actionType = "task_edit",
        Guid? requestedBy = null, string? module = null)
        => new(source, category == InboxCategories.Alert ? "monitor_alert" : "approval", Guid.NewGuid(), category, actionType,
            title, $"{title} subtitle", status, direction, null, "project", project ?? ProjectA, "Payments",
            new InboxPerson(requestedBy ?? Other, "Bala"), new InboxPerson(Me, "Anu"),
            T0, T0.AddMinutes(minutesAfterT0), [], 0, pending,
            module is null ? new Dictionary<string, IReadOnlyList<string>>() : new Dictionary<string, IReadOnlyList<string>> { ["module"] = [module] });

    [Fact]
    public async Task List_merges_every_provider_pending_first_then_most_recently_updated()
    {
        _work.Items.AddRange([Item("work", "old done", 1), Item("work", "new pending", 5, pending: true)]);
        _leave.Items.AddRange([Item("leave", "newest done", 9), Item("leave", "old pending", 2, pending: true)]);

        var page = await Router().ListAsync(TenantId, Me, new InboxQuery(), default);

        page.Items.Select(i => i.Title).Should().Equal("new pending", "old pending", "newest done", "old done");
        page.Total.Should().Be(4);
    }

    [Theory]
    [InlineData(InboxSorts.Newest, new[] { "created 3", "created 2", "created 1" })]
    [InlineData(InboxSorts.Oldest, new[] { "created 1", "created 2", "created 3" })]
    [InlineData("unknown", new[] { "created 1", "created 3", "created 2" })]
    public async Task List_sorts_by_creation_time_when_asked_and_by_latest_activity_otherwise(string sort, string[] expected)
    {
        _work.Items.AddRange([
            Item("work", "created 2", 1) with { CreatedAt = T0.AddDays(2) },
            Item("work", "created 1", 9) with { CreatedAt = T0.AddDays(1) }]);
        _leave.Items.Add(Item("leave", "created 3", 5) with { CreatedAt = T0.AddDays(3) });

        var page = await Router().ListAsync(TenantId, Me, new InboxQuery(Sort: sort), default);

        page.Items.Select(i => i.Title).Should().Equal(expected);
    }

    [Fact]
    public async Task List_pages_across_providers()
    {
        for (var i = 0; i < 3; i++) _work.Items.Add(Item("work", $"w{i}", i * 2));
        for (var i = 0; i < 3; i++) _leave.Items.Add(Item("leave", $"l{i}", i * 2 + 1));

        var page = await Router().ListAsync(TenantId, Me, new InboxQuery(Page: 2, PageSize: 4), default);

        page.Items.Select(i => i.Title).Should().Equal("l0", "w0");
        page.Total.Should().Be(6);
        page.Page.Should().Be(2);
    }

    [Fact]
    public async Task A_source_filter_only_asks_the_matching_providers()
    {
        _work.Items.Add(Item("work", "w", 1));
        _leave.Items.Add(Item("leave", "l", 2));

        var page = await Router().ListAsync(TenantId, Me, new InboxQuery(Source: ["leave"]), default);

        page.Items.Select(i => i.Title).Should().Equal("l");
        _work.ListCalls.Should().Be(0);
    }

    [Fact]
    public async Task Common_filters_apply_to_every_source()
    {
        _work.Items.AddRange([
            Item("work", "alert in B", 1, category: InboxCategories.Alert, direction: InboxDirections.Info, project: ProjectB, status: "open", actionType: "overdue"),
            Item("work", "sent request", 2, direction: InboxDirections.Sent, requestedBy: Me),
            Item("work", "Payments budget", 3, status: "approved", module: "m1"),
        ]);

        async Task<IEnumerable<string>> Titles(InboxQuery q) => (await Router().ListAsync(TenantId, Me, q, default)).Items.Select(i => i.Title);

        (await Titles(new InboxQuery(Category: [InboxCategories.Alert]))).Should().Equal("alert in B");
        (await Titles(new InboxQuery(ContextIds: [ProjectB]))).Should().Equal("alert in B");
        (await Titles(new InboxQuery(Direction: [InboxDirections.Sent]))).Should().Equal("sent request");
        (await Titles(new InboxQuery(Status: ["approved", "open"]))).Should().BeEquivalentTo("alert in B", "Payments budget");
        (await Titles(new InboxQuery(ActionTypes: ["overdue"]))).Should().Equal("alert in B");
        (await Titles(new InboxQuery(RequestedBy: [Me]))).Should().Equal("sent request");
        (await Titles(new InboxQuery(Search: "BUDGET"))).Should().Equal("Payments budget");
        (await Titles(new InboxQuery(Facets: new Dictionary<string, IReadOnlyList<string>> { ["module"] = ["m1"] }))).Should().Equal("Payments budget");
        (await Titles(new InboxQuery(From: T0.AddMinutes(-1), To: T0.AddMinutes(1)))).Should().HaveCount(3);
        (await Titles(new InboxQuery(From: T0.AddDays(1)))).Should().BeEmpty();
    }

    [Fact]
    public async Task A_kind_facet_matches_the_item_kind_even_when_the_provider_sends_no_kind_facet()
    {
        _work.Items.AddRange([Item("work", "request", 1), Item("work", "alert", 2, category: InboxCategories.Alert)]);

        var page = await Router().ListAsync(TenantId, Me,
            new InboxQuery(Facets: new Dictionary<string, IReadOnlyList<string>> { ["kind"] = ["monitor_alert"] }), default);

        page.Items.Select(i => i.Title).Should().Equal("alert");
    }

    [Fact]
    public async Task Unread_message_counts_are_filled_for_the_returned_page()
    {
        var item = Item("work", "w", 1);
        _work.Items.Add(item);
        _unread.Setup(u => u.GetAsync(TenantId, Me, It.IsAny<IReadOnlyCollection<ItemRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ItemRef, int> { [new ItemRef("work", item.Kind, item.Id)] = 3 });

        var page = await Router().ListAsync(TenantId, Me, new InboxQuery(), default);

        page.Items.Single().UnreadMessages.Should().Be(3);
    }

    [Fact]
    public async Task Filters_add_a_source_field_and_merge_same_key_fields_across_providers()
    {
        _work.Fields.Add(new InboxFilterField("project", "Project", true, [new("a", "Alpha"), new("b", "Beta")]));
        _leave.Fields.Add(new InboxFilterField("project", "Project", true, [new("b", "Beta"), new("c", "Gamma")]));
        _leave.Fields.Add(new InboxFilterField("leave_type", "Leave type", true, [new("annual", "Annual")]));

        var fields = await Router().GetFiltersAsync(TenantId, Me, default);

        fields.Select(f => f.Key).Should().Equal("source", "project", "leave_type");
        fields.Single(f => f.Key == "source").Options.Should().Equal(new InboxFilterOption("work", "Work"), new InboxFilterOption("leave", "Leave"));
        fields.Single(f => f.Key == "project").Options.Select(o => o.Value).Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task Counts_pending_received_open_alerts_and_unread_messages()
    {
        var unreadItem = Item("work", "received pending", 1, pending: true);
        _work.Items.AddRange([
            unreadItem,
            Item("work", "sent pending", 2, pending: true, direction: InboxDirections.Sent),
            Item("work", "received done", 3),
            Item("work", "open alert", 4, pending: true, category: InboxCategories.Alert, direction: InboxDirections.Info),
            Item("work", "achieved alert", 5, pending: false, category: InboxCategories.Alert, direction: InboxDirections.Info),
        ]);
        _unread.Setup(u => u.GetAsync(TenantId, Me, It.IsAny<IReadOnlyCollection<ItemRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ItemRef, int> { [new ItemRef("work", unreadItem.Kind, unreadItem.Id)] = 2 });

        var counts = await Router().GetCountsAsync(TenantId, Me, default);

        (counts.PendingReceived, counts.UnreadMessages, counts.OpenAlerts).Should().Be((1, 2, 1));
        // Tab badges: what needs the caller (received pending requests + open alerts), per source and per kind.
        counts.BySource.Should().BeEquivalentTo(new Dictionary<string, int> { ["work"] = 2 });
        counts.ByKind.Should().BeEquivalentTo(new Dictionary<string, int> { ["work:approval"] = 1, ["work:monitor_alert"] = 1 });
    }

    [Fact]
    public async Task Count_maps_split_by_source_and_kind()
    {
        _work.Items.Add(Item("work", "w", 1, pending: true));
        _leave.Items.AddRange([Item("leave", "l1", 2, pending: true), Item("leave", "l2", 3, pending: true),
            Item("leave", "mine", 4, pending: true, direction: InboxDirections.Sent)]);

        var counts = await Router().GetCountsAsync(TenantId, Me, default);

        counts.BySource.Should().BeEquivalentTo(new Dictionary<string, int> { ["work"] = 1, ["leave"] = 2 });
        counts.ByKind!["leave:approval"].Should().Be(2);
    }

    [Fact]
    public async Task Detail_and_action_on_an_unknown_source_are_not_found()
    {
        (await Router().GetDetailAsync(TenantId, Me, "hr", "escalation", Guid.NewGuid(), default)).StatusCode.Should().Be(404);
        (await Router().ExecuteActionAsync(TenantId, Me, "hr", "escalation", Guid.NewGuid(), "approve", null, default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Detail_dispatches_by_source_and_fills_unread()
    {
        var item = Item("work", "w", 1);
        _work.Details[item.Id] = new InboxItemDetail(item, [], "summary", [], null, null, null, null);
        _unread.Setup(u => u.GetAsync(TenantId, Me, It.IsAny<IReadOnlyCollection<ItemRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ItemRef, int> { [new ItemRef("work", item.Kind, item.Id)] = 1 });

        var result = await Router().GetDetailAsync(TenantId, Me, "work", item.Kind, item.Id, default);

        result.Value!.Summary.Should().Be("summary");
        result.Value.Item.UnreadMessages.Should().Be(1);
        (await Router().GetDetailAsync(TenantId, Me, "work", item.Kind, Guid.NewGuid(), default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task A_successful_action_publishes_inbox_changed_to_the_affected_people()
    {
        var id = Guid.NewGuid();
        _work.ActionResult = Result<InboxActionResult>.Success(new InboxActionResult([Me, Other]));

        var result = await Router().ExecuteActionAsync(TenantId, Me, "work", "approval", id, "approve", null, default);

        result.IsSuccess.Should().BeTrue();
        _work.LastAction.Should().Be(("approval", id, "approve"));
        _realtime.Verify(r => r.InboxChangedAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(Me) && ids.Contains(Other)),
            new ItemRef("work", "approval", id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_failed_action_returns_the_provider_error_and_publishes_nothing()
    {
        _work.ActionResult = Result<InboxActionResult>.Forbidden("Only the approver can decide.");

        var result = await Router().ExecuteActionAsync(TenantId, Me, "work", "approval", Guid.NewGuid(), "approve", null, default);

        result.StatusCode.Should().Be(403);
        _realtime.Verify(r => r.InboxChangedAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<ItemRef>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class FakeProvider(string source, string label) : IInboxSourceProvider
    {
        public List<InboxItem> Items { get; } = new();
        public List<InboxFilterField> Fields { get; } = new();
        public Dictionary<Guid, InboxItemDetail> Details { get; } = new();
        public Result<InboxActionResult> ActionResult { get; set; } = Result<InboxActionResult>.Success(new InboxActionResult([]));
        public (string Kind, Guid Id, string Action)? LastAction { get; private set; }
        public Dictionary<Guid, Result<InboxActionResult>> ActionResults { get; } = new();
        public List<(string Kind, Guid Id, string Action, JsonElement? Body)> Actions { get; } = new();
        public int ListCalls { get; private set; }

        public string Source => source;
        public string Label => label;

        public Task<IReadOnlyList<InboxItem>> ListAsync(Guid tenantId, InboxQuery query, Guid callerEmployeeId, CancellationToken ct)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<InboxItem>>(Items);
        }

        public Task<InboxItemDetail?> GetDetailAsync(Guid tenantId, string kind, Guid id, Guid callerEmployeeId, CancellationToken ct)
            => Task.FromResult(Details.GetValueOrDefault(id));

        public Task<IReadOnlyList<InboxFilterField>> GetFilterFieldsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InboxFilterField>>(Fields);

        public Task<Result<InboxActionResult>> ExecuteActionAsync(
            Guid tenantId, string kind, Guid id, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct)
        {
            LastAction = (kind, id, action);
            Actions.Add((kind, id, action, body));
            return Task.FromResult(ActionResults.GetValueOrDefault(id, ActionResult));
        }
    }
}
