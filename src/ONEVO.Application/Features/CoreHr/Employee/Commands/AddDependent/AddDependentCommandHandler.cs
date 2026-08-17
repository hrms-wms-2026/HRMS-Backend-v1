using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddDependent;

public class AddDependentCommandHandler : IRequestHandler<AddDependentCommand, Result<Guid>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public AddDependentCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees, IEmployeeProfileRepository profile, ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(AddDependentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<Guid>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<Guid>.NotFound("No employee record for the current user.");

        var dependent = new EmployeeDependent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            Name = request.Name.Trim(),
            Relationship = request.Relationship,
            DateOfBirth = request.DateOfBirth,
            IsEmergencyContact = request.IsEmergencyContact,
            Phone = request.Phone?.Trim(),
            CreatedById = _currentUser.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _profile.AddDependentAsync(dependent, ct);
        await _profile.SaveChangesAsync(ct);

        return Result<Guid>.Success(dependent.Id);
    }
}
