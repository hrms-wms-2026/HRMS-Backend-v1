using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeIdentity;

/// <summary>Backed by ICallerIdentityResolver - the same coverage-free, permission-free lookup
/// Work Management already uses to resolve project owner/member names and avatars. Deliberately
/// bypasses IEmployeeVisibilityScopeResolver (unlike GetEmployeeQueryHandler): a name and avatar
/// are universal display data, not an HR record, so every authenticated tenant employee can
/// resolve anyone else's here - no role or employees:read permission required.</summary>
public class GetEmployeeIdentityQueryHandler : IRequestHandler<GetEmployeeIdentityQuery, Result<EmployeeIdentityResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;

    public GetEmployeeIdentityQueryHandler(ICurrentUser currentUser, ICallerIdentityResolver identity)
    {
        _currentUser = currentUser;
        _identity = identity;
    }

    public async Task<Result<EmployeeIdentityResponse>> Handle(GetEmployeeIdentityQuery request, CancellationToken ct)
    {
        var identities = await _identity.ResolveIdentitiesByEmployeeIdAsync(_currentUser.TenantId, [request.EmployeeId], ct);

        if (!identities.TryGetValue(request.EmployeeId, out var identity))
            return Result<EmployeeIdentityResponse>.NotFound("The employee could not be found.");

        return Result<EmployeeIdentityResponse>.Success(
            new EmployeeIdentityResponse(request.EmployeeId, identity.Name, identity.AvatarFileId));
    }
}
