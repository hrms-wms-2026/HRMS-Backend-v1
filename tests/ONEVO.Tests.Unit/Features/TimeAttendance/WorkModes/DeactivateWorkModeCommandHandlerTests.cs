using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.DeactivateWorkMode;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.WorkModes;

public class DeactivateWorkModeCommandHandlerTests
{
    private readonly Mock<IWorkModeRepository> _workModes = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private DeactivateWorkModeCommandHandler CreateHandler()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(_tenantId);
        _clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);
        return new DeactivateWorkModeCommandHandler(_workModes.Object, _currentUser.Object, _clock.Object);
    }

    [Fact]
    public async Task Handle_SeededWorkMode_IsAllowedToDeactivate_NoInUseGuard()
    {
        var id = Guid.NewGuid();
        var tracked = new WorkMode
        {
            Id = id, TenantId = _tenantId, LegalEntityId = Guid.NewGuid(),
            Name = "Onsite", IsSystemSeeded = true, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _workModes.Setup(x => x.GetTrackedByIdAsync(_tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tracked);
        var handler = CreateHandler();

        var result = await handler.Handle(new DeactivateWorkModeCommand(id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(tracked.IsActive);
        _workModes.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        _workModes.Setup(x => x.GetTrackedByIdAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkMode?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new DeactivateWorkModeCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
