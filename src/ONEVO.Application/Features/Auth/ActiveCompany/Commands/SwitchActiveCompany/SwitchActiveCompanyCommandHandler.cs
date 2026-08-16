using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;

namespace ONEVO.Application.Features.Auth.ActiveCompany.Commands.SwitchActiveCompany;

/// <summary>
/// Switches which of the caller's own Employee rows (i.e. which legal entity/company) is active
/// for their current session. Permission resolution reads this on the very next request
/// (TenantDatabaseTicketStore.RetrieveAsync runs per-request, not once at login) - no forced
/// re-login or token refresh needed.
/// </summary>
public sealed class SwitchActiveCompanyCommandHandler : IRequestHandler<SwitchActiveCompanyCommand, Result<Unit>>
{
    private readonly ISessionRepository _sessions;
    private readonly IEmployeeRepository _employees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public SwitchActiveCompanyCommandHandler(
        ISessionRepository sessions, IEmployeeRepository employees, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    {
        _sessions = sessions;
        _employees = employees;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<Unit>> Handle(SwitchActiveCompanyCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        if (_currentUser.SessionId is not Guid sessionId)
            return Result<Unit>.Failure("No active session.", 401);

        var session = await _sessions.GetByIdAsync(sessionId, ct);
        if (session is null)
            return Result<Unit>.Failure("No active session.", 401);

        var targetEmployee = await _employees.GetByIdAsync(tenantId, request.EmployeeId, ct)
            ?? await _employees.GetByUserAndLegalEntityAsync(tenantId, _currentUser.UserId, request.EmployeeId, ct);
        if (targetEmployee is null)
            return Result<Unit>.NotFound("The selected company could not be found.");

        if (targetEmployee.UserId != _currentUser.UserId)
            return Result<Unit>.Failure("You do not have access to this company.", 403);

        session.ActiveEmployeeId = targetEmployee.Id;
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }
}
