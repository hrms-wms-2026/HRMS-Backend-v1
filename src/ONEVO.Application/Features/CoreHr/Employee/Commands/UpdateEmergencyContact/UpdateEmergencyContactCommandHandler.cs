using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmergencyContact;

public class UpdateEmergencyContactCommandHandler : IRequestHandler<UpdateEmergencyContactCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public UpdateEmergencyContactCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees, IEmployeeProfileRepository profile, ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(UpdateEmergencyContactCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result.NotFound("No employee record for the current user.");

        var contact = await _profile.GetEmergencyContactAsync(tenantId, employee.Id, request.ContactId, ct);
        if (contact is null)
            return Result.NotFound("Emergency contact not found.");

        contact.Name = request.Name.Trim();
        contact.Relationship = request.Relationship.Trim();
        contact.Phone = request.Phone.Trim();
        contact.Email = request.Email?.Trim();
        contact.IsPrimary = request.IsPrimary;
        contact.UpdatedAt = DateTimeOffset.UtcNow;

        await _profile.SaveChangesAsync(ct);
        return Result.Success();
    }
}
