using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmployeeJobDetails;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class UpdateEmployeeJobDetailsCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IEmployeeOffboardingLockGuard> _offboardingLockGuard = new();
    private readonly Mock<IEmploymentTypeRepository> _employmentTypes = new();
    private readonly Mock<IWorkModeRepository> _workModes = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private UpdateEmployeeJobDetailsCommandHandler CreateHandler() => new(
        _employeeRepository.Object,
        _offboardingLockGuard.Object,
        _employmentTypes.Object,
        _workModes.Object,
        _currentUser.Object);

    private static EmployeeEntity BuildEmployee(Guid tenantId, Guid legalEntityId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        LegalEntityId = legalEntityId,
        EmployeeNumber = "OLD-001",
        FirstName = "Jane",
        LastName = "Doe",
        Email = "jane.doe@example.com",
        EmploymentTypeId = 1,
        EmploymentStatusId = 1,
        HireDate = new DateOnly(2025, 1, 1),
    };

    private void SetUpHappyPath(Guid tenantId, EmployeeEntity employee)
    {
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _employeeRepository
            .Setup(r => r.GetTrackedByIdAsync(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _offboardingLockGuard
            .Setup(g => g.EnsureMutable(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result?)null);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(tenantId, "NEW-001", employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _employmentTypes
            .Setup(r => r.GetIdByCodeAsync("part_time", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
    }

    [Fact]
    public async Task Handle_ValidRequest_UpdatesEmployeeAndSaves()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = BuildEmployee(tenantId, legalEntityId);
        SetUpHappyPath(tenantId, employee);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new UpdateEmployeeJobDetailsCommand(employee.Id, "NEW-001", "part_time", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("NEW-001", employee.EmployeeNumber);
        Assert.Equal(2, employee.EmploymentTypeId);
        Assert.Null(employee.WorkModeId);
        _employeeRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidWorkModeInSameLegalEntity_UpdatesWorkModeId()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = BuildEmployee(tenantId, legalEntityId);
        SetUpHappyPath(tenantId, employee);
        var workMode = new WorkMode
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityId,
            Name = "Remote", IsActive = true
        };
        _workModes
            .Setup(r => r.GetByIdAsync(tenantId, workMode.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workMode);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new UpdateEmployeeJobDetailsCommand(employee.Id, "NEW-001", "part_time", workMode.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(workMode.Id, employee.WorkModeId);
    }

    [Fact]
    public async Task Handle_WorkModeFromDifferentLegalEntity_ReturnsFailure_DoesNotSave()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = BuildEmployee(tenantId, legalEntityId);
        SetUpHappyPath(tenantId, employee);
        var workMode = new WorkMode
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = Guid.NewGuid(),
            Name = "Remote", IsActive = true
        };
        _workModes
            .Setup(r => r.GetByIdAsync(tenantId, workMode.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workMode);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new UpdateEmployeeJobDetailsCommand(employee.Id, "NEW-001", "part_time", workMode.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        _employeeRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveWorkMode_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = BuildEmployee(tenantId, legalEntityId);
        SetUpHappyPath(tenantId, employee);
        var workMode = new WorkMode
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityId,
            Name = "Remote", IsActive = false
        };
        _workModes
            .Setup(r => r.GetByIdAsync(tenantId, workMode.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workMode);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new UpdateEmployeeJobDetailsCommand(employee.Id, "NEW-001", "part_time", workMode.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_DuplicateEmployeeNumber_ReturnsConflict_DoesNotSave()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = BuildEmployee(tenantId, legalEntityId);
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _employeeRepository
            .Setup(r => r.GetTrackedByIdAsync(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _offboardingLockGuard
            .Setup(g => g.EnsureMutable(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result?)null);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(tenantId, "TAKEN-001", employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new UpdateEmployeeJobDetailsCommand(employee.Id, "TAKEN-001", "full_time", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _employeeRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidEmployeeNumberFormat_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = BuildEmployee(tenantId, legalEntityId);
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _employeeRepository
            .Setup(r => r.GetTrackedByIdAsync(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _offboardingLockGuard
            .Setup(g => g.EnsureMutable(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new UpdateEmployeeJobDetailsCommand(employee.Id, "bad number!", "full_time", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UnknownEmploymentTypeCode_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = BuildEmployee(tenantId, legalEntityId);
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _employeeRepository
            .Setup(r => r.GetTrackedByIdAsync(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _offboardingLockGuard
            .Setup(g => g.EnsureMutable(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result?)null);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(tenantId, "NEW-001", employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _employmentTypes
            .Setup(r => r.GetIdByCodeAsync("bogus", It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new UpdateEmployeeJobDetailsCommand(employee.Id, "NEW-001", "bogus", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_EmployeeOffboardingLocked_ReturnsConflict_DoesNotSave()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = BuildEmployee(tenantId, legalEntityId);
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _employeeRepository
            .Setup(r => r.GetTrackedByIdAsync(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _offboardingLockGuard
            .Setup(g => g.EnsureMutable(tenantId, employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Conflict("This employee is being offboarded and cannot be edited."));

        var handler = CreateHandler();
        var result = await handler.Handle(
            new UpdateEmployeeJobDetailsCommand(employee.Id, "NEW-001", "full_time", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _employeeRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmployeeNotFound_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _employeeRepository
            .Setup(r => r.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new UpdateEmployeeJobDetailsCommand(employeeId, "NEW-001", "full_time", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
