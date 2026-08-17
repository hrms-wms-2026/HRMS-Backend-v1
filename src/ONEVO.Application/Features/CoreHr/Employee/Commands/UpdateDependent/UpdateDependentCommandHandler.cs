using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateDependent;

public class UpdateDependentCommandHandler : IRequestHandler<UpdateDependentCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public UpdateDependentCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees, IEmployeeProfileRepository profile, ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(UpdateDependentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result.NotFound("No employee record for the current user.");

        var dependent = await _profile.GetDependentAsync(tenantId, employee.Id, request.DependentId, ct);
        if (dependent is null)
            return Result.NotFound("Dependent not found.");

        dependent.Name = request.Name.Trim();
        dependent.Relationship = request.Relationship;
        dependent.DateOfBirth = request.DateOfBirth;
        dependent.IsEmergencyContact = request.IsEmergencyContact;
        dependent.Phone = request.Phone?.Trim();
        dependent.UpdatedAt = DateTimeOffset.UtcNow;

        await _profile.SaveChangesAsync(ct);
        return Result.Success();
    }
}
