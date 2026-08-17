using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;

/// <summary>
/// Minimal capacity-checked position reassignment - not the full approval-routed Promotion/
/// Transfer workflow described in the OneVo-HR docs (that workflow doesn't exist in this
/// codebase; per explicit product decision this is the deliberately smaller version). Reuses the
/// same atomic seat-reservation SQL pattern as onboarding invitations, adapted to create the new
/// assignment "active" immediately rather than "planned".
/// </summary>
public class ChangeEmployeePositionCommandHandler : IRequestHandler<ChangeEmployeePositionCommand, Result<Unit>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IPositionAssignmentRepository _positionAssignmentRepository;
    private readonly ICurrentUser _currentUser;

    public ChangeEmployeePositionCommandHandler(
        IEmployeeRepository employeeRepository,
        IPositionRepository positionRepository,
        IPositionAssignmentRepository positionAssignmentRepository,
        ICurrentUser currentUser)
    {
        _employeeRepository = employeeRepository;
        _positionRepository = positionRepository;
        _positionAssignmentRepository = positionAssignmentRepository;
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

        var reservedAssignmentId = await _positionAssignmentRepository.TryCreateActiveAssignmentAsync(
            tenantId, employee.Id, position.Id, request.EffectiveFrom, _currentUser.UserId, ct);
        if (reservedAssignmentId is null)
            return Result<Unit>.Conflict("This position has reached its capacity.");

        var currentAssignment = await _positionAssignmentRepository.GetActivePrimaryAsync(tenantId, employee.Id, ct);
        if (currentAssignment is not null)
        {
            var effectiveTo = request.EffectiveFrom.AddDays(-1);
            if (effectiveTo < currentAssignment.EffectiveFrom)
                effectiveTo = currentAssignment.EffectiveFrom;
            await _positionAssignmentRepository.EndActiveAsync(tenantId, currentAssignment.Id, effectiveTo, ct);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}
