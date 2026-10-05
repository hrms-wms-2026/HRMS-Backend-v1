using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.Queries.GetMyLeaveWorkWindow;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Balance;

public class GetMyLeaveWorkWindowQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsWorkWindowFromEmployeeLegalEntity()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            LegalEntityId = legalEntityId,
            FirstName = "Priya",
            LastName = "Nair",
            EmployeeNumber = "E1",
            HireDate = new DateOnly(2024, 1, 1)
        };
        var legalEntity = new LegalEntity
        {
            Id = legalEntityId,
            TenantId = tenantId,
            Name = "Acme",
            WorkStartTime = new TimeOnly(9, 0),
            WorkEndTime = new TimeOnly(18, 0),
            BreakDurationMinutes = 60,
            Timezone = "Asia/Colombo"
        };

        var currentUser = new Mock<ICurrentUser>();
        var employees = new Mock<IEmployeeRepository>();
        var legalEntities = new Mock<ILegalEntityRepository>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        legalEntities.Setup(x => x.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntity);

        var handler = new GetMyLeaveWorkWindowQueryHandler(currentUser.Object, employees.Object, legalEntities.Object);
        var result = await handler.Handle(new GetMyLeaveWorkWindowQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkStartTime.Should().Be(new TimeOnly(9, 0));
        result.Value.WorkEndTime.Should().Be(new TimeOnly(18, 0));
        result.Value.BreakDurationMinutes.Should().Be(60);
        result.Value.Timezone.Should().Be("Asia/Colombo");
    }

    [Fact]
    public async Task Handle_WhenLegalEntityUnset_ReturnsEmptyWindow()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            LegalEntityId = null,
            FirstName = "Priya",
            LastName = "Nair",
            EmployeeNumber = "E1",
            HireDate = new DateOnly(2024, 1, 1)
        };

        var currentUser = new Mock<ICurrentUser>();
        var employees = new Mock<IEmployeeRepository>();
        var legalEntities = new Mock<ILegalEntityRepository>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var handler = new GetMyLeaveWorkWindowQueryHandler(currentUser.Object, employees.Object, legalEntities.Object);
        var result = await handler.Handle(new GetMyLeaveWorkWindowQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkStartTime.Should().BeNull();
        result.Value.WorkEndTime.Should().BeNull();
        legalEntities.Verify(
            x => x.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
