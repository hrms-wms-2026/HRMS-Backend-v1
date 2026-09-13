using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.CreateWorkMode;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.WorkModes;

public class CreateWorkModeCommandHandlerTests
{
    private readonly Mock<IWorkModeRepository> _workModes = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    private CreateWorkModeCommandHandler CreateHandler()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);
        _legalEntities.Setup(x => x.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
        return new CreateWorkModeCommandHandler(_workModes.Object, _legalEntities.Object, _currentUser.Object, _clock.Object);
    }

    [Fact]
    public async Task Handle_AtFiveActiveWorkModes_ReturnsConflict()
    {
        _workModes.Setup(x => x.CountActiveAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _workModes.Setup(x => x.NameExistsAsync(_tenantId, _legalEntityId, "Field", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new CreateWorkModeCommand(_legalEntityId, "Field", false, true, false, false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _workModes.Verify(x => x.AddAsync(It.IsAny<Domain.Features.TimeAttendance.Entities.WorkMode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateNameInSameLegalEntity_ReturnsConflict()
    {
        _workModes.Setup(x => x.CountActiveAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _workModes.Setup(x => x.NameExistsAsync(_tenantId, _legalEntityId, "Remote", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new CreateWorkModeCommand(_legalEntityId, "Remote", false, true, false, false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ValidRequest_AddsWorkModeAndSaves()
    {
        _workModes.Setup(x => x.CountActiveAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _workModes.Setup(x => x.NameExistsAsync(_tenantId, _legalEntityId, "Client Site", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new CreateWorkModeCommand(_legalEntityId, "Client Site", true, true, false, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Client Site", result.Value!.Name);
        Assert.True(result.Value.PhotoRequired);
        Assert.False(result.Value.IsSystemSeeded);
        _workModes.Verify(x => x.AddAsync(It.IsAny<Domain.Features.TimeAttendance.Entities.WorkMode>(), It.IsAny<CancellationToken>()), Times.Once);
        _workModes.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
