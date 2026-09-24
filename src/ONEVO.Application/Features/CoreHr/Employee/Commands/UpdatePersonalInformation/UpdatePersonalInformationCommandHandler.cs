using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;

public class UpdatePersonalInformationCommandHandler : IRequestHandler<UpdatePersonalInformationCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeRepository _featureEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly ICurrentUser _currentUser;

    public UpdatePersonalInformationCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IEmployeeRepository featureEmployees,
        IEmployeeProfileRepository profile,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _featureEmployees = featureEmployees;
        _profile = profile;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(UpdatePersonalInformationCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var lookup = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (lookup is null)
            return Result.NotFound("No employee record for the current user.");

        var tracked = await _featureEmployees.GetTrackedByIdAsync(tenantId, lookup.Id, ct);
        if (tracked is null)
            return Result.NotFound("No employee record for the current user.");

        tracked.FirstName = request.FirstName.Trim();
        tracked.LastName = request.LastName.Trim();
        tracked.Phone = request.Phone?.Trim();
        tracked.DateOfBirth = request.DateOfBirth;
        tracked.Gender = request.Gender;
        tracked.NationalityId = request.NationalityId;
        tracked.DisplayTimezone = request.DisplayTimezone;
        tracked.UpdatedAt = DateTimeOffset.UtcNow;

        _featureEmployees.SetExpectedVersion(tracked, request.Version);

        _profile.ReplaceAddresses(tenantId, tracked.Id, request.Addresses
            .Select(a => new EmployeeAddress
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = tracked.Id,
                AddressType = a.AddressType,
                AddressJson = a.AddressJson,
                IsPrimary = a.IsPrimary,
                CreatedById = _currentUser.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            }).ToList());

        try
        {
            await _profile.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Conflict("This profile was just updated elsewhere. Please refresh and try again.");
        }

        return Result.Success();
    }
}
