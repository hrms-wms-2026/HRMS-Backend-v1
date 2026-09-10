using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class LeaveApprovalDelegateCommandTests
{
    [Fact]
    public async Task Create_RejectsCoveringYourself()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
            FirstName = "Priya", LastName = "Nair", EmployeeNumber = "E1", HireDate = new DateOnly(2024, 1, 1)
        };
        var currentUser = new Mock<ICurrentUser>();
        var employees = new Mock<IEmployeeRepository>();
        var requests = new Mock<ILeaveRequestRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var handler = new CreateLeaveApprovalDelegateCommandHandler(
            currentUser.Object, employees.Object, requests.Object, unitOfWork.Object);
        var result = await handler.Handle(
            new CreateLeaveApprovalDelegateCommand(employee.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cannot cover your own");
    }

    [Fact]
    public void Validator_RejectsEndBeforeStart()
    {
        var validator = new CreateLeaveApprovalDelegateCommandValidator();
        var result = validator.Validate(new CreateLeaveApprovalDelegateCommand(
            Guid.NewGuid(), new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 9)));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Create_RejectsOverlappingCover()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
            FirstName = "Priya", LastName = "Nair", EmployeeNumber = "E1", HireDate = new DateOnly(2024, 1, 1)
        };
        var coverId = Guid.NewGuid();
        var cover = new Employee
        {
            Id = coverId, TenantId = tenantId,
            FirstName = "Sam", LastName = "Lee", EmployeeNumber = "E2", HireDate = new DateOnly(2024, 1, 1)
        };
        var currentUser = new Mock<ICurrentUser>();
        var employees = new Mock<IEmployeeRepository>();
        var requests = new Mock<ILeaveRequestRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        employees.Setup(x => x.GetByIdAsync(tenantId, coverId, It.IsAny<CancellationToken>())).ReturnsAsync(cover);
        requests.Setup(x => x.ListDelegatesForApproverAsync(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new LeaveApprovalDelegateListRow(
                    Guid.NewGuid(), coverId, "Sam Lee", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15))
            ]);

        var handler = new CreateLeaveApprovalDelegateCommandHandler(
            currentUser.Object, employees.Object, requests.Object, unitOfWork.Object);
        var result = await handler.Handle(
            new CreateLeaveApprovalDelegateCommand(coverId, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }
}
