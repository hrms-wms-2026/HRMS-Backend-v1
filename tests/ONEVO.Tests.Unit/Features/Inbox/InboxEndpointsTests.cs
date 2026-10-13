using FluentAssertions;
using Moq;
using ONEVO.Api.Controllers.Tenant.Inbox;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Commands.ExecuteInboxAction;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Queries.GetInboxCounts;
using ONEVO.Application.Features.Inbox.Queries.ListInboxItems;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

public class InboxEndpointsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IInboxRouter> _router = new();

    public InboxEndpointsTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
    }

    private InboxCallerResolver Caller() => new(_currentUser.Object, _identity.Object);

    [Fact]
    public void The_list_request_maps_query_string_values_and_facets()
    {
        var project = Guid.NewGuid();
        var request = new InboxListRequest
        {
            Category = ["alert"], Project = [project], Type = ["task_edit"], Search = "budget",
            Facet = ["module:m1", "module:m2", "module:m1", "broken", ":x", "label:"], Page = 2, PageSize = 10
        };

        var query = request.ToQuery();

        query.Category.Should().Equal("alert");
        query.ContextIds.Should().Equal(project);
        query.ActionTypes.Should().Equal("task_edit");
        query.Search.Should().Be("budget");
        query.Facets.Should().ContainSingle().Which.Value.Should().Equal("m1", "m2");
        (query.Page, query.PageSize).Should().Be((2, 10));
    }

    [Fact]
    public async Task List_resolves_the_caller_employee_and_asks_the_router()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
        var page = new InboxPage([], 0, 1, 25);
        var query = new InboxQuery();
        _router.Setup(r => r.ListAsync(TenantId, EmployeeId, query, It.IsAny<CancellationToken>())).ReturnsAsync(page);

        var result = await new ListInboxItemsQueryHandler(Caller(), _router.Object).Handle(new ListInboxItemsQuery(query), default);

        result.Value.Should().BeSameAs(page);
    }

    [Fact]
    public async Task Callers_without_an_employee_record_are_forbidden_everywhere()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        (await new GetInboxCountsQueryHandler(Caller(), _router.Object).Handle(new GetInboxCountsQuery(), default)).StatusCode.Should().Be(403);
        (await new ExecuteInboxActionCommandHandler(Caller(), _router.Object)
            .Handle(new ExecuteInboxActionCommand("work", "approval", Guid.NewGuid(), "approve", null), default)).StatusCode.Should().Be(403);
        _router.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_unauthenticated_session_is_forbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);

        (await new GetInboxCountsQueryHandler(Caller(), _router.Object).Handle(new GetInboxCountsQuery(), default)).StatusCode.Should().Be(403);
        _identity.VerifyNoOtherCalls();
    }
}
