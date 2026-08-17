using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class ChangeEmployeePositionCommandHandlerTests
{
    // Feature IEmployeeRepository (not Common): GetTrackedByIdAsync lives there.
    // ApproveAccessGrantRequestCommandHandler.cs imports
    // ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly IUnitOfWork _unitOfWork = new FakeUnitOfWork();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IPermissionRepository> _permissionRepository = new();
    private readonly Mock<IAccessGrantRequestRepository> _accessGrantRequestRepository = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();

    private ChangeEmployeePositionCommandHandler CreateHandler() =>
        new(
            _employees.Object,
            _positions.Object,
            _assignments.Object,
            _unitOfWork,
            _currentUser.Object,
            _permissionRepository.Object,
            _accessGrantRequestRepository.Object,
            _clock.Object,
            _outboxWriter.Object);

    private void SetupNonSelfCaller(Guid tenantId, Guid employeeId)
    {
        var callerUserId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(callerUserId);
        _employees
            .Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee
            {
                Id = employeeId,
                TenantId = tenantId,
                UserId = employeeUserId,
                LegalEntityId = Guid.NewGuid(),
            });
    }

    [Fact]
    public async Task Handle_PositionAtCapacity_ReturnsConflict_AfterAttemptingCreate()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();
        var oldAssignmentId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _employees.Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = Guid.NewGuid() });
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), newPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = newPositionId, TenantId = tenantId, IsActive = true });
        _assignments
            .Setup(a => a.GetActivePrimaryAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment
            {
                Id = oldAssignmentId,
                TenantId = tenantId,
                EmployeeId = employeeId,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
        _assignments
            .Setup(a => a.EndActiveAsync(tenantId, oldAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _assignments
            .Setup(a => a.TryCreateActiveAssignmentAsync(tenantId, employeeId, newPositionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow), "Promotion"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _assignments.Verify(
            a => a.TryCreateActiveAssignmentAsync(tenantId, employeeId, newPositionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _assignments.Verify(
            a => a.EndActiveAsync(tenantId, oldAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SeatAvailable_EndsCurrentAssignmentAndCreatesNew()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();
        var oldAssignmentId = Guid.NewGuid();
        var newAssignmentId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _employees.Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = Guid.NewGuid() });
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), newPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = newPositionId, TenantId = tenantId, IsActive = true });
        _assignments
            .Setup(a => a.GetActivePrimaryAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment
            {
                Id = oldAssignmentId,
                TenantId = tenantId,
                EmployeeId = employeeId,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
        _assignments
            .Setup(a => a.EndActiveAsync(tenantId, oldAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _assignments
            .Setup(a => a.TryCreateActiveAssignmentAsync(tenantId, employeeId, newPositionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAssignmentId);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow), "Promotion"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _assignments.Verify(a => a.EndActiveAsync(tenantId, oldAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        _assignments.Verify(
            a => a.TryCreateActiveAssignmentAsync(tenantId, employeeId, newPositionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PositionNotFoundInEmployeesLegalEntity_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _employees.Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, UserId = Guid.NewGuid(), LegalEntityId = Guid.NewGuid() });
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), newPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Position?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow), "Promotion"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UniqueConstraintConflictOnCreate_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();
        var oldAssignmentId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _employees.Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = Guid.NewGuid() });
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), newPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = newPositionId, TenantId = tenantId, IsActive = true });
        _assignments
            .Setup(a => a.GetActivePrimaryAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment
            {
                Id = oldAssignmentId,
                TenantId = tenantId,
                EmployeeId = employeeId,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
        _assignments
            .Setup(a => a.EndActiveAsync(tenantId, oldAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _assignments
            .Setup(a => a.TryCreateActiveAssignmentAsync(tenantId, employeeId, newPositionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintConflictException(new Exception("duplicate key value violates unique constraint")));

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow), "Promotion"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("refresh", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_EndActiveReturnsFalse_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();
        var oldAssignmentId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _employees.Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = Guid.NewGuid() });
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), newPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = newPositionId, TenantId = tenantId, IsActive = true });
        _assignments
            .Setup(a => a.GetActivePrimaryAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment
            {
                Id = oldAssignmentId,
                TenantId = tenantId,
                EmployeeId = employeeId,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
        _assignments
            .Setup(a => a.EndActiveAsync(tenantId, oldAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow), "Promotion"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _assignments.Verify(
            a => a.TryCreateActiveAssignmentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_TargetIsCallersOwnEmployee_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _employees
            .Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, UserId = userId, LegalEntityId = Guid.NewGuid() });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), "Promotion"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("You cannot change your own position.", result.Error);
        _positions.Verify(p => p.GetByIdForLegalEntityAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SensitivePosition_NoApprovers_ReturnsUnprocessable()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        SetupNonSelfCaller(tenantId, employeeId);
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, TenantId = tenantId, IsActive = true, DepartmentId = Guid.NewGuid() });
        _positions.Setup(p => p.GetAccessTemplateByPositionAsync(tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionAccessTemplate { Id = Guid.NewGuid(), RequiresApproval = true, RoleId = Guid.NewGuid() });
        _permissionRepository.Setup(p => p.ListUserIdsWithPermissionCodeAsync(tenantId, "roles:manage", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), "Promotion"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_SensitivePosition_CreatesAccessGrantRequest_DoesNotTouchCurrentAssignment()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        SetupNonSelfCaller(tenantId, employeeId);
        var accessTemplate = new PositionAccessTemplate { Id = Guid.NewGuid(), RequiresApproval = true, RoleId = Guid.NewGuid() };
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, TenantId = tenantId, IsActive = true, DepartmentId = Guid.NewGuid() });
        _positions.Setup(p => p.GetAccessTemplateByPositionAsync(tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessTemplate);
        _permissionRepository.Setup(p => p.ListUserIdsWithPermissionCodeAsync(tenantId, "roles:manage", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { approverUserId });
        _assignments
            .Setup(a => a.TryReservePositionAssignmentAsync(tenantId, employeeId, positionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), "Promotion"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _accessGrantRequestRepository.Verify(r => r.AddAsync(
            It.Is<AccessGrantRequest>(g => g.ActionType == AccessGrantActionType.PositionChange && g.EmployeeId == employeeId),
            It.IsAny<CancellationToken>()), Times.Once);
        _assignments.Verify(a => a.EndActiveAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
