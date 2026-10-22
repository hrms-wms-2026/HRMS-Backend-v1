using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Type.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class UpdateLeaveTypeCommandHandlerTests
{
    private readonly Mock<ILeaveTypeRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();
    private readonly DateTimeOffset _fixedTime = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    public UpdateLeaveTypeCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_fixedTime);
    }

    [Fact]
    public async Task Handle_ExistingType_UpdatesFieldsAndReturnsSuccess()
    {
        var entity = new LeaveType
        {
            Id = _leaveTypeId, TenantId = _tenantId, Name = "Annual Leave", Code = "ANNUAL",
            Color = "#3B82F6",
            ApplicableGender = LeaveGenderRestrictions.All, IsActive = true
        };
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "Annual Leave (Updated)", _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new UpdateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);
        var command = new UpdateLeaveTypeCommand(
            _leaveTypeId, "Annual Leave (Updated)", "Updated description", "#EF4444",
            true, LeaveGenderRestrictions.All, true, 16m, ["pdf"]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Annual Leave (Updated)", result.Value!.Name);
        Assert.Equal("#EF4444", result.Value.Color);
        Assert.Equal(16m, result.Value.DocumentRequiredAfterHours);
        Assert.Equal("ANNUAL", result.Value.Code);
        _repoMock.Verify(r => r.Update(entity), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync((LeaveType?)null);
        var handler = new UpdateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);
        var command = new UpdateLeaveTypeCommand(
            _leaveTypeId, "X", null, "#3B82F6", true, LeaveGenderRestrictions.All, false, null, []);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
