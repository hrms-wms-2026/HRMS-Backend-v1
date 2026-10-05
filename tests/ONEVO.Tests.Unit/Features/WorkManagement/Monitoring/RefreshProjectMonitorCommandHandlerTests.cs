using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Commands.RefreshProjectMonitor;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

public class RefreshProjectMonitorCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 9, 29);

    private readonly Mock<IProjectMonitorCallerResolver> _callers = new();
    private readonly Mock<IProjectMonitorService> _monitor = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public RefreshProjectMonitorCommandHandlerTests() => _clock.SetupGet(x => x.Today).Returns(Today);

    private RefreshProjectMonitorCommandHandler Handler() => new(_callers.Object, _monitor.Object, _unitOfWork.Object, _clock.Object);

    [Fact]
    public async Task ProjectMember_ReEvaluatesTheProjectNow_AndSaves()
    {
        _callers.Setup(x => x.ResolveAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MonitorCaller>.Success(new MonitorCaller(TenantId, Guid.NewGuid(), new Project { Id = ProjectId })));
        _monitor.Setup(x => x.EvaluateProjectAsync(TenantId, ProjectId, Today, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Handler().Handle(new RefreshProjectMonitorCommand(ProjectId), default);

        Assert.True(result.IsSuccess);
        _monitor.Verify(x => x.EvaluateProjectAsync(TenantId, ProjectId, Today, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoAccess_DoesNothing()
    {
        _callers.Setup(x => x.ResolveAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MonitorCaller>.Forbidden("nope"));

        var result = await Handler().Handle(new RefreshProjectMonitorCommand(ProjectId), default);

        Assert.Equal(403, result.StatusCode);
        _monitor.VerifyNoOtherCalls();
        _unitOfWork.VerifyNoOtherCalls();
    }
}
