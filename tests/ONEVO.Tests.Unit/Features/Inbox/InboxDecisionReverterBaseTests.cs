using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Revert;
using ONEVO.Domain.Features.Inbox.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

public class InboxDecisionReverterBaseTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly ItemRef Item = new("leave", "request", Guid.NewGuid());

    private readonly InMemoryInboxDecisionJournal _journal = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public InboxDecisionReverterBaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<InboxActionResult>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<InboxActionResult>>> op, CancellationToken ct) => op(ct));
    }

    private TestReverter Reverter() => new(_journal, _uow.Object, _clock.Object);

    [Fact]
    public async Task The_actor_can_undo_their_latest_decision_within_30_minutes_and_a_revert_row_is_recorded()
    {
        var decision = _journal.Add(TenantId, Item, InboxJournalActions.Approve, Me, Now.AddMinutes(-29), afterJson: """{"status":"approved"}""");
        var reverter = Reverter();

        var result = await reverter.RevertAsync(TenantId, Item, Me, "oops", default);

        result.IsSuccess.Should().BeTrue();
        reverter.Applied.Should().ContainSingle().Which.Should().Be(decision.Id);
        var revert = _journal.Entries.Single(e => e.Action == InboxJournalActions.Revert);
        decision.RevertedByJournalId.Should().Be(revert.Id);
        revert.BeforeJson.Should().Be("""{"status":"approved"}""");
        revert.Comment.Should().Be("oops");
    }

    [Fact]
    public async Task Someone_other_than_the_actor_cannot_undo()
    {
        _journal.Add(TenantId, Item, InboxJournalActions.Approve, Other, Now.AddMinutes(-1));

        var result = await Reverter().RevertAsync(TenantId, Item, Me, null, default);

        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(InboxDecisionReverterBase.NotYourDecision);
    }

    [Fact]
    public async Task After_31_minutes_the_window_is_closed()
    {
        _journal.Add(TenantId, Item, InboxJournalActions.Approve, Me, Now.AddMinutes(-31));

        var result = await Reverter().RevertAsync(TenantId, Item, Me, null, default);

        result.StatusCode.Should().Be(409);
        result.Error.Should().Be(InboxDecisionReverterBase.WindowClosed);
    }

    [Fact]
    public async Task Only_the_items_latest_decision_can_be_undone()
    {
        _journal.Add(TenantId, Item, InboxJournalActions.Forward, Me, Now.AddMinutes(-10));
        _journal.Add(TenantId, Item, InboxJournalActions.Approve, Other, Now.AddMinutes(-5));

        var result = await Reverter().RevertAsync(TenantId, Item, Me, null, default);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task An_already_reverted_decision_cannot_be_undone_twice()
    {
        _journal.Add(TenantId, Item, InboxJournalActions.Approve, Me, Now.AddMinutes(-5));
        var reverter = Reverter();
        (await reverter.RevertAsync(TenantId, Item, Me, null, default)).IsSuccess.Should().BeTrue();

        var again = await reverter.RevertAsync(TenantId, Item, Me, null, default);

        again.StatusCode.Should().Be(409);
        again.Error.Should().Be(InboxDecisionReverterBase.AlreadyReverted);
    }

    [Fact]
    public async Task A_failed_dependency_check_blocks_the_revert_without_touching_the_module()
    {
        _journal.Add(TenantId, Item, InboxJournalActions.Approve, Me, Now.AddMinutes(-5));
        var reverter = Reverter();
        reverter.Dependencies = Result.Conflict("The leave has already started.");

        var result = await reverter.RevertAsync(TenantId, Item, Me, null, default);
        var can = await reverter.CanRevertAsync(TenantId, Item, Me, default);

        result.StatusCode.Should().Be(409);
        result.Error.Should().Be("The leave has already started.");
        can.IsSuccess.Should().BeFalse();
        reverter.Applied.Should().BeEmpty();
        _journal.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task A_module_refusal_during_the_rollback_returns_its_error_and_records_nothing()
    {
        _journal.Add(TenantId, Item, InboxJournalActions.Approve, Me, Now.AddMinutes(-5));
        var reverter = Reverter();
        reverter.ApplyResult = Result<InboxActionResult>.Conflict("The balance can no longer cover this request.");

        var result = await reverter.RevertAsync(TenantId, Item, Me, null, default);

        result.StatusCode.Should().Be(409);
        _journal.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Nothing_to_undo_is_not_found()
        => (await Reverter().RevertAsync(TenantId, Item, Me, null, default)).StatusCode.Should().Be(404);

    private sealed class TestReverter(IInboxDecisionJournal journal, IUnitOfWork uow, IDateTimeProvider clock)
        : InboxDecisionReverterBase(journal, uow, clock)
    {
        public Result Dependencies { get; set; } = Result.Success();
        public Result<InboxActionResult> ApplyResult { get; set; } = Result<InboxActionResult>.Success(new InboxActionResult([Guid.NewGuid()]));
        public List<Guid> Applied { get; } = new();

        public override string Source => "leave";
        public override string Kind => "request";

        protected override Task<Result> CheckDependenciesAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct)
            => Task.FromResult(Dependencies);

        protected override Task<Result<InboxActionResult>> ApplyRevertAsync(
            Guid tenantId, InboxDecisionJournalEntry decision, Guid callerEmployeeId, CancellationToken ct)
        {
            if (ApplyResult.IsSuccess)
                Applied.Add(decision.Id);
            return Task.FromResult(ApplyResult);
        }
    }
}
