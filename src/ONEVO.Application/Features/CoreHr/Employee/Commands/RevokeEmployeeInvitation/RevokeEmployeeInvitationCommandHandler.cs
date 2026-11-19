using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Access;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;

/// <summary>
/// Revoke from the employee detail screen or Settings > Users (pairs with ResendEmployeeInvitation),
/// allowed for the person who added the employee or users:manage (IEmployeeInvitationAccess).
/// Marks the current invitation token revoked and cancels its reserved seat (if one exists),
/// freeing the position's capacity for a different candidate. Unlike Resend, this works on a
/// still-pending invitation too - revoke is the explicit "stop, don't let this person in" action,
/// not limited to expired tokens.
/// </summary>
public sealed class RevokeEmployeeInvitationCommandHandler
    : IRequestHandler<RevokeEmployeeInvitationCommand, Result<Unit>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IInvitationTokenRepository _invitationTokenRepository;
    private readonly IPositionAssignmentRepository _positionAssignmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IEmployeeInvitationAccess _invitationAccess;

    public RevokeEmployeeInvitationCommandHandler(
        IEmployeeRepository employeeRepository,
        IInvitationTokenRepository invitationTokenRepository,
        IPositionAssignmentRepository positionAssignmentRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IEmployeeInvitationAccess invitationAccess)
    {
        _employeeRepository = employeeRepository;
        _invitationTokenRepository = invitationTokenRepository;
        _positionAssignmentRepository = positionAssignmentRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _invitationAccess = invitationAccess;
    }

    public async Task<Result<Unit>> Handle(RevokeEmployeeInvitationCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var current = await _invitationTokenRepository.GetLatestByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (current is null)
            return Result<Unit>.Failure("No invitation has been sent to this employee.", 400);

        var employee = await _employeeRepository.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<Unit>.NotFound("The employee could not be found.");
        if (!await _invitationAccess.CanManageAsync(current, employee.CreatedById, ct))
            return Result<Unit>.Forbidden(
                "Only the person who added this employee, or a user manager, can revoke the invitation.");

        if (current.UsedAt is not null)
            return Result<Unit>.Failure("This invitation has already been accepted.", 400);
        if (current.RevokedAt is not null)
            return Result<Unit>.Failure("This invitation has already been revoked.", 400);

        current.RevokedAt = _clock.UtcNow;
        current.RevokedById = _currentUser.UserId;

        if (current.PositionAssignmentId is Guid reservedAssignmentId)
            await _positionAssignmentRepository.CancelPlannedAsync(tenantId, reservedAssignmentId, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }
}
