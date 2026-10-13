using FluentAssertions;
using Moq;
using ONEVO.Api.Controllers.Tenant.Inbox;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Commands.ExecuteInboxAction;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Queries.GetInboxCounts;
using ONEVO.Application.Features.Inbox.Queries.GetInboxFilters;
using ONEVO.Application.Features.Inbox.Queries.GetInboxItemDetail;
using ONEVO.Application.Common.Models;
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
    public async Task Filters_and_detail_go_to_the_router_for_the_callers_employee()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
        IReadOnlyList<InboxFilterField> fields = [new InboxFilterField("source", "Source", true, [])];
        _router.Setup(r => r.GetFiltersAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(fields);
        var id = Guid.NewGuid();
        _router.Setup(r => r.GetDetailAsync(TenantId, EmployeeId, "work", "approval", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<InboxItemDetail>.NotFound("Inbox item not found."));

        (await new GetInboxFiltersQueryHandler(Caller(), _router.Object).Handle(new GetInboxFiltersQuery(), default)).Value.Should().BeSameAs(fields);
        (await new GetInboxItemDetailQueryHandler(Caller(), _router.Object).Handle(new GetInboxItemDetailQuery("work", "approval", id), default))
            .StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Filters_and_detail_are_forbidden_without_an_employee_record()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        (await new GetInboxFiltersQueryHandler(Caller(), _router.Object).Handle(new GetInboxFiltersQuery(), default)).StatusCode.Should().Be(403);
        (await new GetInboxItemDetailQueryHandler(Caller(), _router.Object).Handle(new GetInboxItemDetailQuery("work", "approval", Guid.NewGuid()), default))
            .StatusCode.Should().Be(403);
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
