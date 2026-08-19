using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Lookups;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class CompleteOffboardingCommandHandlerTests
{
    private readonly Mock<IOffboardingRecordRepository> _offboardingRecordRepository = new();
    private readonly Mock<IEmployeeChecklistTaskRepository> _taskRepository = new();
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<ISessionRepository> _sessionRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IEmployeeOffboardingCoverageGuard> _coverageGuard = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public CompleteOffboardingCommandHandlerTests()
    {
        _currentUser.Setup(c => c.TenantId).Returns(_tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
    }

    private CompleteOffboardingCommandHandler CreateSut() => new(
        _offboardingRecordRepository.Object, _taskRepository.Object, _employeeRepository.Object,
        _userRepository.Object, _sessionRepository.Object, _unitOfWork.Object, _coverageGuard.Object, _currentUser.Object, _clock.Object);

    [Fact]
    public async Task Handle_RequiredTaskStillPending_ReturnsUnprocessableEntity()
    {
        var record = new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress, Reason = "resignation" };
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _taskRepository.Setup(r => r.ListByOffboardingRecordAsync(_tenantId, record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeChecklistTask> { new() { IsRequired = true, Status = EmployeeChecklistTaskStatuses.Pending } });

        var result = await CreateSut().Handle(new CompleteOffboardingCommand(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(422);
        _sessionRepository.Verify(r => r.RevokeAllActiveByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ResignationReason_MapsToResignedAndCompletesFully()
    {
        var userId = Guid.NewGuid();
        var record = new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress, Reason = "resignation", LastWorkingDate = new DateOnly(2026, 12, 1) };
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _taskRepository.Setup(r => r.ListByOffboardingRecordAsync(_tenantId, record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeChecklistTask> { new() { IsRequired = true, Status = EmployeeChecklistTaskStatuses.Completed } });
        var employee = new EmployeeEntity { Id = _employeeId, UserId = userId, EmploymentStatusId = EmploymentStatusIds.Offboarding };
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        var user = new User { Id = userId, IsActive = true };
        _userRepository.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await CreateSut().Handle(new CompleteOffboardingCommand(_employeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        employee.EmploymentStatusId.Should().Be(EmploymentStatusIds.Resigned);
        employee.TerminationDate.Should().Be(record.LastWorkingDate);
        user.IsActive.Should().BeFalse();
        record.Status.Should().Be(OffboardingRecordStatuses.Completed);
        _sessionRepository.Verify(r => r.RevokeAllActiveByUserIdAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TerminationReason_MapsToTerminated()
    {
        var userId = Guid.NewGuid();
        var record = new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress, Reason = "termination", LastWorkingDate = new DateOnly(2026, 12, 1) };
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _taskRepository.Setup(r => r.ListByOffboardingRecordAsync(_tenantId, record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<EmployeeChecklistTask>());
        var employee = new EmployeeEntity { Id = _employeeId, UserId = userId };
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        _userRepository.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = userId, IsActive = true });

        await CreateSut().Handle(new CompleteOffboardingCommand(_employeeId), CancellationToken.None);

        employee.EmploymentStatusId.Should().Be(EmploymentStatusIds.Terminated);
    }
}
