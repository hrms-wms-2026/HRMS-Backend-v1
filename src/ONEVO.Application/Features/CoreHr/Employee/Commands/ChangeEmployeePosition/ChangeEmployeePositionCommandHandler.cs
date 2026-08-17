using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;

/// <summary>
/// Minimal capacity-checked position reassignment - not the full approval-routed Promotion/
/// Transfer workflow described in the OneVo-HR docs (that workflow doesn't exist in this
/// codebase; per explicit product decision this is the deliberately smaller version). Reuses the
/// same atomic seat-reservation SQL pattern as onboarding invitations, adapted to create the new
/// assignment "active" immediately rather than "planned".
///
/// Mutation order is end-then-create inside one transaction so the unique index
/// ix_position_assignments_one_active_primary_per_employee is never violated; a failed create
/// rolls back the EndActive via <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>.
/// Sensitive positions (access template RequiresApproval) reserve a Planned seat and create an
/// AccessGrantRequest instead of ending the current assignment.
/// </summary>
public class ChangeEmployeePositionCommandHandler : IRequestHandler<ChangeEmployeePositionCommand, Result<ChangeEmployeePositionResponse>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IPositionAssignmentRepository _positionAssignmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IAccessGrantRequestRepository _accessGrantRequestRepository;
    private readonly IDateTimeProvider _clock;
    private readonly IOutboxWriter _outboxWriter;

    public ChangeEmployeePositionCommandHandler(
        IEmployeeRepository employeeRepository,
        IPositionRepository positionRepository,
        IPositionAssignmentRepository positionAssignmentRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IPermissionRepository permissionRepository,
        IAccessGrantRequestRepository accessGrantRequestRepository,
        IDateTimeProvider clock,
        IOutboxWriter outboxWriter)
    {
        _employeeRepository = employeeRepository;
        _positionRepository = positionRepository;
        _positionAssignmentRepository = positionAssignmentRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _permissionRepository = permissionRepository;
        _accessGrantRequestRepository = accessGrantRequestRepository;
        _clock = clock;
        _outboxWriter = outboxWriter;
    }

    public async Task<Result<ChangeEmployeePositionResponse>> Handle(ChangeEmployeePositionCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var employee = await _employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<ChangeEmployeePositionResponse>.NotFound("The employee could not be found.");

        if (employee.UserId == _currentUser.UserId)
            return Result<ChangeEmployeePositionResponse>.Forbidden("You cannot change your own position.");

        if (employee.LegalEntityId is not Guid legalEntityId)
            return Result<ChangeEmployeePositionResponse>.UnprocessableEntity("This employee has no assigned legal entity.");

        var position = await _positionRepository.GetByIdForLegalEntityAsync(tenantId, legalEntityId, request.PositionId, ct);
        if (position is null || !position.IsActive)
            return Result<ChangeEmployeePositionResponse>.NotFound("The selected position does not exist or is not active in this employee's company.");

        var accessTemplate = await _positionRepository.GetAccessTemplateByPositionAsync(tenantId, position.Id, ct);
        if (accessTemplate is { RequiresApproval: true })
        {
            var approverUserIds = await _permissionRepository.ListUserIdsWithPermissionCodeAsync(
                tenantId, "roles:manage", _clock.UtcNow, ct);
            if (approverUserIds.Count == 0)
                return Result<ChangeEmployeePositionResponse>.UnprocessableEntity(
                    "No one currently holds the permission required to approve this request.");

            var reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
                tenantId, employee.Id, position.Id, request.EffectiveFrom, _currentUser.UserId, ct);
            if (reservedAssignmentId is null)
                return Result<ChangeEmployeePositionResponse>.Conflict("This position has reached its capacity.");

            var grantRequest = new AccessGrantRequest
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employee.Id,
                ActionType = AccessGrantActionType.PositionChange,
                TargetPositionId = position.Id,
                TargetDepartmentId = position.DepartmentId!.Value,
                PositionAccessTemplateId = accessTemplate.Id,
                RequestedRoleId = accessTemplate.RoleId,
                ApprovalStatus = "Pending",
                RequestedByUserId = _currentUser.UserId,
                RequestedAt = _clock.UtcNow,
                EffectiveFrom = request.EffectiveFrom.ToDateTime(TimeOnly.MinValue),
                ReservedPositionAssignmentId = reservedAssignmentId,
                ChangeReason = request.ChangeReason,
            };
            await _accessGrantRequestRepository.AddAsync(grantRequest, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            foreach (var approverUserId in approverUserIds)
                await EnqueuePositionChangeApprovalEmailAsync(tenantId, approverUserId, grantRequest, employee, position, ct);

            return Result<ChangeEmployeePositionResponse>.Success(new ChangeEmployeePositionResponse(PendingApproval: true));
        }

        var currentAssignment = await _positionAssignmentRepository.GetActivePrimaryAsync(tenantId, employee.Id, ct);

        try
        {
            await _unitOfWork.ExecuteInTransactionAsync(async txnCt =>
            {
                if (currentAssignment is not null)
                {
                    var effectiveTo = request.EffectiveFrom.AddDays(-1);
                    if (effectiveTo < currentAssignment.EffectiveFrom)
                        effectiveTo = currentAssignment.EffectiveFrom;
                    var ended = await _positionAssignmentRepository.EndActiveAsync(
                        tenantId, currentAssignment.Id, effectiveTo, txnCt);
                    if (!ended)
                    {
                        // Another concurrent change already ended this active primary.
                        throw new UniqueConstraintConflictException(
                            new InvalidOperationException("Active primary assignment was already ended."));
                    }
                }

                var reservedAssignmentId = await _positionAssignmentRepository.TryCreateActiveAssignmentAsync(
                    tenantId, employee.Id, position.Id, request.EffectiveFrom, _currentUser.UserId, txnCt);
                if (reservedAssignmentId is null)
                    throw new PositionAtCapacityException();

                return true;
            }, ct);
        }
        catch (PositionAtCapacityException)
        {
            return Result<ChangeEmployeePositionResponse>.Conflict("This position has reached its capacity.");
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<ChangeEmployeePositionResponse>.Conflict(
                "This employee's position was just changed by someone else. Please refresh and try again.");
        }

        return Result<ChangeEmployeePositionResponse>.Success(new ChangeEmployeePositionResponse(PendingApproval: false));
    }

    private Task EnqueuePositionChangeApprovalEmailAsync(
        Guid tenantId,
        Guid approverUserId,
        AccessGrantRequest grantRequest,
        ONEVO.Domain.Features.CoreHr.Entities.Employee employee,
        Position position,
        CancellationToken ct)
    {
        _ = _outboxWriter;
        return Task.CompletedTask;
    }

    private sealed class PositionAtCapacityException : Exception;
}
