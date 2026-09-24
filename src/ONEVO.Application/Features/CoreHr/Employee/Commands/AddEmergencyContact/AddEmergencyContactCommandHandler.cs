using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddEmergencyContact;

public class AddEmergencyContactCommandHandler : IRequestHandler<AddEmergencyContactCommand, Result<Guid>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public AddEmergencyContactCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees, IEmployeeProfileRepository profile, ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(AddEmergencyContactCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<Guid>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<Guid>.NotFound("No employee record for the current user.");

        var contact = new EmployeeEmergencyContact
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            Name = request.Name.Trim(),
            Relationship = request.Relationship.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email?.Trim(),
            IsPrimary = request.IsPrimary,
            CreatedById = _currentUser.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _profile.AddEmergencyContactAsync(contact, ct);
        await _profile.SaveChangesAsync(ct);

        return Result<Guid>.Success(contact.Id);
    }
}
