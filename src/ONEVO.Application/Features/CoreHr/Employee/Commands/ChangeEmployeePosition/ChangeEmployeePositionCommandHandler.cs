using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
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
/// </summary>
public class ChangeEmployeePositionCommandHandler : IRequestHandler<ChangeEmployeePositionCommand, Result<Unit>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IPositionAssignmentRepository _positionAssignmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public ChangeEmployeePositionCommandHandler(
        IEmployeeRepository employeeRepository,
        IPositionRepository positionRepository,
        IPositionAssignmentRepository positionAssignmentRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser)
    {
        _employeeRepository = employeeRepository;
        _positionRepository = positionRepository;
        _positionAssignmentRepository = positionAssignmentRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<Unit>> Handle(ChangeEmployeePositionCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var employee = await _employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<Unit>.NotFound("The employee could not be found.");

        if (employee.LegalEntityId is not Guid legalEntityId)
            return Result<Unit>.UnprocessableEntity("This employee has no assigned legal entity.");

        var position = await _positionRepository.GetByIdForLegalEntityAsync(tenantId, legalEntityId, request.PositionId, ct);
        if (position is null || !position.IsActive)
            return Result<Unit>.NotFound("The selected position does not exist or is not active in this employee's company.");

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
                    await _positionAssignmentRepository.EndActiveAsync(
                        tenantId, currentAssignment.Id, effectiveTo, txnCt);
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
            return Result<Unit>.Conflict("This position has reached its capacity.");
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private sealed class PositionAtCapacityException : Exception;
}
