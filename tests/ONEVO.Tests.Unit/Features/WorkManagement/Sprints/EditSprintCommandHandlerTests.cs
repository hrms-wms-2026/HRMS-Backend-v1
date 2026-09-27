using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class EditSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private (EditSprintCommandHandler Handler, Sprint Sprint, Mock<ISprintActivityLogRepository> Logs) Build(
        string sprintStatus, Guid? callerEmployeeId = null, bool? callerCanManage = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var sprint = new Sprint
        {
            Id = SprintId, TenantId = TenantId, ProjectId = ProjectId, Name = "Old",
            StartDate = sprintStatus == SprintStatuses.Draft ? null : new DateOnly(2026, 9, 1),
            EndDate = sprintStatus == SprintStatuses.Draft ? null : new DateOnly(2026, 9, 14),
            Status = sprintStatus, CreatedAt = DateTimeOffset.UtcNow
        };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var access = new Mock<ISprintAccessService>();
        access.Setup(x => x.CanManageAsync(TenantId, sprint, UserId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerCanManage ?? (resolvedCallerEmployeeId == OwnerEmployeeId));

        var logs = new Mock<ISprintActivityLogRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new EditSprintCommandHandler(currentUser.Object, identity.Object, sprints.Object, access.Object, logs.Object, unitOfWork.Object);
        return (handler, sprint, logs);
    }

    [Fact]
    public async Task Handle_ActiveSprint_UpdatesFields()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Active);
        var command = new EditSprintCommand(SprintId, "New Name", "Goal", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 16));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", sprint.Name);
        Assert.Equal(new DateOnly(2026, 9, 16), sprint.EndDate);
    }

    [Theory]
    [InlineData(SprintStatuses.Complete)]
    [InlineData(SprintStatuses.Achieved)]
    public async Task Handle_TerminalSprint_ReturnsConflict(string status)
    {
        var (handler, sprint, _) = Build(status);
        var command = new EditSprintCommand(SprintId, "New Name", "Goal", sprint.StartDate!.Value, sprint.EndDate!.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Old", sprint.Name);
    }

    [Fact]
    public async Task Handle_CannotManage_ReturnsForbidden()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId, callerCanManage: false);
        var command = new EditSprintCommand(SprintId, "New Name", "Goal", sprint.StartDate!.Value, sprint.EndDate!.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Old", sprint.Name);
    }

    [Fact]
    public async Task Handle_CallerCanManageViaTaskModuleOwnership_EditsSprint()
    {
        // Caller is not the sprint's creator, but CanManageAsync reports them able to manage via
        // task-module ownership - the service's own logic is unit-tested separately, so this only
        // proves the handler defers to its answer.
        var (handler, sprint, _) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId, callerCanManage: true);
        var command = new EditSprintCommand(SprintId, "New Name", "Goal", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 16));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", sprint.Name);
    }

    [Fact]
    public async Task Handle_DraftSprint_DatesProvided_ReturnsFailure()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Draft);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DraftSprint_NameAndGoalOnly_Succeeds()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Draft);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", sprint.Name);
        Assert.Equal("Goal", sprint.Goal);
        Assert.Null(sprint.StartDate);
    }

    [Fact]
    public async Task Handle_ActiveSprint_DatesProvided_UpdatesThem()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Active);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 20));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 5), sprint.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 20), sprint.EndDate);
    }

    [Fact]
    public async Task Handle_ActiveSprint_EndBeforeStart_ReturnsFailure()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Active);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 5));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_Edit_WritesEditedLog()
    {
        var (handler, sprint, logs) = Build(SprintStatuses.Active);
        var command = new EditSprintCommand(SprintId, "New Name", "Goal", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 16));

        await handler.Handle(command, CancellationToken.None);

        logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l =>
            l.Action == SprintActivityActions.Edited && l.FromStatus == null && l.ToStatus == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
