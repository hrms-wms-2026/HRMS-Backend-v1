using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.ListProjectWorkApprovals;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ListProjectWorkApprovalsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();

    public ListProjectWorkApprovalsQueryHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Requester] = "Bala" });
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[]
            {
                new Objective { Id = _root, OwnerId = Guid.NewGuid(), IsDefault = true },
                new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = A },
                new Objective { Id = _other, ParentObjectiveId = _root, OwnerId = Guid.NewGuid() },
            }));
    }

    private WorkApprovalRequest Pending(Guid position) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, PositionObjectiveId = position,
        ApproverSource = WorkApprovalSources.Hierarchy, RequestedByEmployeeId = Requester,
        Status = WorkApprovalRequestStatuses.Pending, ActionType = WorkActionTypes.TaskEdit,
        TargetType = WorkTargetTypes.Task, TargetTitle = "t"
    };

    private ListProjectWorkApprovalsQueryHandler Build() => new(_currentUser.Object, _identity.Object, _requests.Object, _hierarchy.Object);

    [Fact]
    public async Task Inbox_ReturnsOnlyPendingTheCallerCanDecide()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(A);
        var mine = Pending(_p);
        var notMine = Pending(_other);
        _requests.Setup(x => x.ListByProjectAsync(TenantId, ProjectId, null, WorkApprovalRequestStatuses.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalRequest> { mine, notMine });

        var result = await Build().Handle(new ListProjectWorkApprovalsQuery(ProjectId, "inbox", null), default);

        result.Value!.Select(r => r.Id).Should().Equal(mine.Id);
        result.Value![0].RequestedByName.Should().Be("Bala");
    }

    [Fact]
    public async Task Mine_ReturnsCallerRequests_WithStatusFilter()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Requester);
        _requests.Setup(x => x.ListByProjectAsync(TenantId, ProjectId, Requester, "approved", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalRequest> { Pending(_p) });

        var result = await Build().Handle(new ListProjectWorkApprovalsQuery(ProjectId, "mine", "approved"), default);

        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task UnknownScope_Returns400()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(A);
        (await Build().Handle(new ListProjectWorkApprovalsQuery(ProjectId, "everything", null), default)).StatusCode.Should().Be(400);
    }
}
