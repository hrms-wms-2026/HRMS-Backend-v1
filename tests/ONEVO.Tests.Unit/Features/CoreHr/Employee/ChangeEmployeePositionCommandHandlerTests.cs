using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
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

    private ChangeEmployeePositionCommandHandler CreateHandler() =>
        new(_employees.Object, _positions.Object, _assignments.Object, _unitOfWork, _currentUser.Object);

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
            .Setup(a => a.TryCreateActiveAssignmentAsync(tenantId, employeeId, newPositionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow)),
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
            .Setup(a => a.TryCreateActiveAssignmentAsync(tenantId, employeeId, newPositionId, It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAssignmentId);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow)),
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
        _employees.Setup(e => e.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = Guid.NewGuid() });
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), newPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Position?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(employeeId, newPositionId, DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
