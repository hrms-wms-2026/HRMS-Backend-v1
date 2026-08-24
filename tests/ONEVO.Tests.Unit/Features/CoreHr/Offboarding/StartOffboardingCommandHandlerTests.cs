using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class StartOffboardingCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IOffboardingRecordRepository> _offboardingRecordRepository = new();
    private readonly Mock<IEmployeeOffboardingCoverageGuard> _coverageGuard = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _actingUserId = Guid.NewGuid();

    public StartOffboardingCommandHandlerTests()
    {
        _currentUser.Setup(c => c.TenantId).Returns(_tenantId);
        _currentUser.Setup(c => c.UserId).Returns(_actingUserId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
    }

    private StartOffboardingCommandHandler CreateSut() =>
        new(_employeeRepository.Object, _offboardingRecordRepository.Object, _coverageGuard.Object, _currentUser.Object, _clock.Object);

    private StartOffboardingCommand CreateCommand() =>
        new(_employeeId, "resignation", new DateOnly(2026, 12, 1), "medium", "eligible", "Notice period completed.");

    [Fact]
    public async Task Handle_EmployeeNotFound_ReturnsNotFound()
    {
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);

        var result = await CreateSut().Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_SelfOffboarding_ReturnsForbidden()
    {
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, UserId = _actingUserId });

        var result = await CreateSut().Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_AlreadyHasOpenOffboarding_ReturnsConflict()
    {
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, UserId = Guid.NewGuid() });
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress });

        var result = await CreateSut().Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesRecordAndSetsOffboardingStatus()
    {
        var employee = new EmployeeEntity { Id = _employeeId, UserId = Guid.NewGuid(), EmploymentStatusId = EmploymentStatusIds.Active };
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OffboardingRecord?)null);

        OffboardingRecord? added = null;
        _offboardingRecordRepository.Setup(r => r.AddAsync(It.IsAny<OffboardingRecord>(), It.IsAny<CancellationToken>()))
            .Callback<OffboardingRecord, CancellationToken>((r, _) => added = r)
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.Reason.Should().Be("resignation");
        added.PreviousEmploymentStatusId.Should().Be(EmploymentStatusIds.Active);
        employee.EmploymentStatusId.Should().Be(EmploymentStatusIds.Offboarding);
        _offboardingRecordRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
