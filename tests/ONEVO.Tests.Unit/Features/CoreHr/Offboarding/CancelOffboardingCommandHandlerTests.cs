using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CancelOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class CancelOffboardingCommandHandlerTests
{
    [Fact]
    public async Task Handle_RevertsEmploymentStatus_AndCancelsRecord()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var employeeRepository = new Mock<IEmployeeRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var record = new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress, PreviousEmploymentStatusId = EmploymentStatusIds.OnLeave };
        offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var employee = new EmployeeEntity { Id = employeeId, EmploymentStatusId = EmploymentStatusIds.Offboarding };
        employeeRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var result = await new CancelOffboardingCommandHandler(offboardingRecordRepository.Object, employeeRepository.Object, Mock.Of<IEmployeeOffboardingCoverageGuard>(), currentUser.Object, clock.Object)
            .Handle(new CancelOffboardingCommand(employeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        employee.EmploymentStatusId.Should().Be(EmploymentStatusIds.OnLeave);
        record.Status.Should().Be(OffboardingRecordStatuses.Cancelled);
    }

    [Fact]
    public async Task Handle_NullPreviousStatus_FallsBackToActive()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var employeeRepository = new Mock<IEmployeeRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var record = new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.Initiated, PreviousEmploymentStatusId = null };
        offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var employee = new EmployeeEntity { Id = employeeId };
        employeeRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        await new CancelOffboardingCommandHandler(offboardingRecordRepository.Object, employeeRepository.Object, Mock.Of<IEmployeeOffboardingCoverageGuard>(), currentUser.Object, clock.Object)
            .Handle(new CancelOffboardingCommand(employeeId), CancellationToken.None);

        employee.EmploymentStatusId.Should().Be(EmploymentStatusIds.Active);
    }
}
