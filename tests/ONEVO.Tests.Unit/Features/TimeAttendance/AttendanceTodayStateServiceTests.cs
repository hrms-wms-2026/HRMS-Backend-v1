using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceTodayStateServiceTests
{
    [Fact]
    public async Task ResolveContextAsync_WithExplicitIdentity_IgnoresCurrentUser()
    {
        var explicitTenantId = Guid.NewGuid();
        var explicitUserId = Guid.NewGuid();
        var currentUserTenantId = Guid.NewGuid();
        var currentUserUserId = Guid.NewGuid();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.IsAuthenticated).Returns(true);
        currentUser.Setup(c => c.TenantId).Returns(currentUserTenantId);
        currentUser.Setup(c => c.UserId).Returns(currentUserUserId);

        var employees = new Mock<IEmployeeRepository>();
        var expectedEmployee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = explicitTenantId,
            UserId = explicitUserId,
            LegalEntityId = Guid.NewGuid()
        };
        employees.Setup(e => e.GetDefaultForUserAsync(explicitTenantId, explicitUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEmployee);
        // GetDefaultForUserAsync for the currentUser ids is deliberately not set up — if the
        // implementation wrongly falls back to ICurrentUser, Moq's default (null) makes
        // ResolveContextAsync return NotFound and the assertion below fails.

        var sut = CreateSut(currentUser.Object, employees.Object);

        var result = await sut.ResolveContextAsync(explicitTenantId, explicitUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedEmployee.Id, result.Value!.Employee.Id);
    }

    [Fact]
    public async Task ResolveContextAsync_Parameterless_StillDelegatesToCurrentUser()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.IsAuthenticated).Returns(true);
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(userId);

        var employees = new Mock<IEmployeeRepository>();
        var expectedEmployee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            LegalEntityId = Guid.NewGuid()
        };
        employees.Setup(e => e.GetDefaultForUserAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEmployee);

        var sut = CreateSut(currentUser.Object, employees.Object);

        var result = await sut.ResolveContextAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedEmployee.Id, result.Value!.Employee.Id);
    }

    [Fact]
    public async Task ResolveContextAsync_Parameterless_NotAuthenticated_ReturnsForbidden()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.IsAuthenticated).Returns(false);
        var employees = new Mock<IEmployeeRepository>();

        var sut = CreateSut(currentUser.Object, employees.Object);

        var result = await sut.ResolveContextAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    private static AttendanceTodayStateService CreateSut(
        ICurrentUser currentUser,
        IEmployeeRepository employees)
    {
        var legalEntity = new ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Timezone = "Asia/Colombo",
            StandardWorkingDays = "[1,2,3,4,5]",
            WorkStartTime = new(9, 0),
            WorkEndTime = new(17, 30),
            BreakDurationMinutes = 60
        };
        var legalEntities = new Mock<ILegalEntityRepository>();
        legalEntities.Setup(x => x.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid legalEntityId, CancellationToken _) =>
                legalEntityId == legalEntity.Id ? legalEntity : null);

        var policies = new Mock<IClockInPolicyRepository>();
        policies.Setup(x => x.ListByLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var attendance = new Mock<IAttendanceReadRepository>();

        var authority = new Mock<IEmployeeAuthorityResolver>();

        var expectedWorkAreas = new Mock<IExpectedWorkAreaResolver>();
        expectedWorkAreas
            .Setup(x => x.ResolveAsync(It.IsAny<Employee>(), It.IsAny<ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExpectedWorkAreaResolution>.Success(
                new ExpectedWorkAreaResolution("remote", legalEntity.Timezone!, "active_employee_work_mode")));

        var dateTime = new Mock<IDateTimeProvider>();
        dateTime.Setup(x => x.UtcNow).Returns(DateTimeOffset.Parse("2026-08-21T10:00:00+00:00"));

        // GetDefaultForUserAsync resolves an employee whose LegalEntityId matches our fixture legal entity
        // for any employee it returns without a preset LegalEntityId — but tests above set it explicitly
        // via the returned Employee, so wire the legal entity lookup to always resolve for that id too.
        legalEntities.Setup(x => x.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntity);

        return new AttendanceTodayStateService(
            currentUser,
            dateTime.Object,
            employees,
            legalEntities.Object,
            policies.Object,
            attendance.Object,
            authority.Object,
            expectedWorkAreas.Object);
    }
}
