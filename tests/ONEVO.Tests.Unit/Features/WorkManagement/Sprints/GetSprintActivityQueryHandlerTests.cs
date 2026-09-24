using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetSprintActivity;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public sealed class GetSprintActivityQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<ISprintRepository> _sprints = new();
    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IPermissionResolver> _permissions = new();
    private readonly Mock<ISprintActivityLogRepository> _logs = new();

    public GetSprintActivityQueryHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);

        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);
    }

    private static Sprint Sprint() => new()
    {
        Id = SprintId,
        TenantId = TenantId,
        ProjectId = ProjectId,
        Name = "Sprint 1",
        Status = SprintStatuses.Active,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static SprintActivityLog Log(string action) => new()
    {
        Id = Guid.NewGuid(),
        SprintId = SprintId,
        EmployeeId = EmployeeId,
        Action = action,
        FromStatus = "draft",
        ToStatus = "active",
        DetailsJson = null,
        OccurredAt = DateTimeOffset.UtcNow
    };

    private GetSprintActivityQueryHandler Build() => new(
        _currentUser.Object, _identity.Object, _sprints.Object, _members.Object, _permissions.Object, _logs.Object);

    [Fact]
    public async Task Member_ReturnsLogsInRepositoryOrder()
    {
        var first = Log(SprintActivityActions.Created);
        var second = Log(SprintActivityActions.Started);
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sprint());
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _logs.Setup(x => x.GetForSprintAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SprintActivityLog> { first, second });

        var result = await Build().Handle(new GetSprintActivityQuery(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(first.Id, result.Value[0].Id);
        Assert.Equal(second.Id, result.Value[1].Id);
        Assert.Equal(first.Action, result.Value[0].Action);
        Assert.Equal(first.EmployeeId, result.Value[0].EmployeeId);
        Assert.Equal(first.FromStatus, result.Value[0].FromStatus);
        Assert.Equal(first.ToStatus, result.Value[0].ToStatus);
        Assert.Equal(first.DetailsJson, result.Value[0].DetailsJson);
        Assert.Equal(first.OccurredAt, result.Value[0].OccurredAt);
    }

    [Fact]
    public async Task ReadPermissionNonMember_Succeeds()
    {
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sprint());
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "projects:read" });
        _logs.Setup(x => x.GetForSprintAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SprintActivityLog>());

        var result = await Build().Handle(new GetSprintActivityQuery(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _members.Verify(x => x.HasActiveMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NonMemberWithoutPermission_ReturnsForbidden()
    {
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sprint());
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Build().Handle(new GetSprintActivityQuery(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _logs.Verify(x => x.GetForSprintAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingSprint_ReturnsNotFound()
    {
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sprint?)null);

        var result = await Build().Handle(new GetSprintActivityQuery(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
