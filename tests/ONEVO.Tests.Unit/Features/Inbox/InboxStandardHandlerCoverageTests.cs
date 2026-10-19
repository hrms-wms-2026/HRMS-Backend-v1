using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Commands.ExecuteInboxBulkAction;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Queries.GetInboxHistory;
using ONEVO.Application.Features.Inbox.Queries.GetInboxKinds;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

/// <summary>Plan BZ coverage audit: the History, kinds and bulk endpoints address the caller's own employee record.</summary>
public sealed class InboxStandardHandlerCoverageTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IInboxRouter> _router = new();

    public InboxStandardHandlerCoverageTests()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
    }

    private InboxCallerResolver Caller(Guid? employeeId)
    {
        _identity.Setup(i => i.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId);
        return new InboxCallerResolver(_currentUser.Object, _identity.Object);
    }

    [Fact]
    public async Task History_is_the_callers_own_decisions_with_the_filters_passed_through()
    {
        var query = new InboxHistoryQuery(Source: "leave", Page: 2, PageSize: 10);
        var page = new InboxHistoryPage([], 0, 2, 10);
        _router.Setup(r => r.GetHistoryAsync(TenantId, EmployeeId, query, It.IsAny<CancellationToken>())).ReturnsAsync(page);

        var result = await new GetInboxHistoryQueryHandler(Caller(EmployeeId), _router.Object).Handle(new GetInboxHistoryQuery(query), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(page);
    }

    [Fact]
    public async Task History_and_bulk_are_forbidden_without_an_employee_record_and_never_reach_the_router()
    {
        var caller = Caller(null);

        var history = await new GetInboxHistoryQueryHandler(caller, _router.Object).Handle(new GetInboxHistoryQuery(new InboxHistoryQuery()), default);
        var bulk = await new ExecuteInboxBulkActionCommandHandler(caller, _router.Object)
            .Handle(new ExecuteInboxBulkActionCommand(new InboxBulkRequest("approve", [new ItemRef("leave", "request", Guid.NewGuid())])), default);

        (history.StatusCode, history.Error).Should().Be((403, InboxCallerResolver.NoEmployeeMessage));
        (bulk.StatusCode, bulk.Error).Should().Be((403, InboxCallerResolver.NoEmployeeMessage));
        _router.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_forbidden_too()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);

        var history = await new GetInboxHistoryQueryHandler(Caller(EmployeeId), _router.Object).Handle(new GetInboxHistoryQuery(new InboxHistoryQuery()), default);

        history.StatusCode.Should().Be(403);
        _identity.Verify(i => i.ResolveCallerEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Bulk_returns_the_routers_per_item_outcome_as_is()
    {
        var ok = new ItemRef("leave", "request", Guid.NewGuid());
        var bad = new ItemRef("leave", "request", Guid.NewGuid());
        var request = new InboxBulkRequest("approve", [ok, bad], Comment: "fine");
        var outcome = new InboxBulkResult([ok], [new InboxBulkFailure(bad, "Already decided.")]);
        _router.Setup(r => r.ExecuteBulkAsync(TenantId, EmployeeId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Result<InboxBulkResult>.Success(outcome));

        var result = await new ExecuteInboxBulkActionCommandHandler(Caller(EmployeeId), _router.Object).Handle(new ExecuteInboxBulkActionCommand(request), default);

        result.Value.Should().BeSameAs(outcome);
    }

    [Fact]
    public async Task Kinds_are_the_registry_as_the_router_reports_it()
    {
        IReadOnlyList<InboxKindDescriptor> kinds = [new InboxKindDescriptor("leave", "request", "Leave request", InboxCategories.Alert, InboxCapabilities.Chat)];
        _router.Setup(r => r.GetKinds()).Returns(kinds);

        var result = await new GetInboxKindsQueryHandler(_router.Object).Handle(new GetInboxKindsQuery(), default);

        result.Value.Should().BeSameAs(kinds);
    }
}
