using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class CreateLeaveTypeCommandHandlerTests
{
    private readonly Mock<ILeaveTypeRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly DateTimeOffset _fixedTime = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    public CreateLeaveTypeCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_fixedTime);
    }

    private static CreateLeaveTypeCommand DefaultCommand(string name = "Annual Leave", string code = "ANNUAL") =>
        new(name, code, "Standard annual leave", LeaveTypeCategories.Annual,
            IsPaid: true, RequiresApproval: true, RequiresDocument: false,
            DocumentRequiredAfterDays: null, AcceptedDocumentTypes: [],
            MaxConsecutiveDays: null, DefaultDaysPerYear: 20m,
            CarryForwardAllowed: true, MaxCarryForwardDays: 5m, CarryForwardExpiryMonths: 3,
            ProRataForNewJoiners: true, ApplicableGender: LeaveGenderRestrictions.All,
            MinimumNoticeDays: 0);

    [Fact]
    public async Task Handle_ValidCommand_CreatesLeaveTypeAndReturnsSuccess()
    {
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "Annual Leave", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repoMock.Setup(r => r.ExistsByCodeAsync(_tenantId, "ANNUAL", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new CreateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Annual Leave", result.Value!.Name);
        Assert.Equal("ANNUAL", result.Value.Code);
        _repoMock.Verify(r => r.AddAsync(It.Is<Domain.Features.Leave.Type.Entities.LeaveType>(
            t => t.TenantId == _tenantId && t.Name == "Annual Leave" && t.Code == "ANNUAL"), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsConflict()
    {
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "Annual Leave", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new CreateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _repoMock.Verify(r => r.AddAsync(It.IsAny<Domain.Features.Leave.Type.Entities.LeaveType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsConflict()
    {
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "Annual Leave", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repoMock.Setup(r => r.ExistsByCodeAsync(_tenantId, "ANNUAL", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new CreateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _repoMock.Verify(r => r.AddAsync(It.IsAny<Domain.Features.Leave.Type.Entities.LeaveType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_LowercaseCode_IsStoredUppercase()
    {
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "Annual Leave", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repoMock.Setup(r => r.ExistsByCodeAsync(_tenantId, "ANNUAL", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new CreateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(DefaultCommand(code: "annual"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ANNUAL", result.Value!.Code);
    }
}
