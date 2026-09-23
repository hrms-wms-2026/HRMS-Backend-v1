using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingWorkModes.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.OnboardingWorkModes.Queries.ListOnboardingWorkModes;

/// <summary>
/// Active work modes for a legal entity, for the Add Employee wizard's work-mode picker. Reads
/// the same tenant-scoped WorkMode entity the Clock-in Policy screen manages (not a global
/// lookup) so the wizard's options always match what the tenant configured.
/// </summary>
public class ListOnboardingWorkModesQueryHandler
    : IRequestHandler<ListOnboardingWorkModesQuery, Result<IReadOnlyList<OnboardingWorkModeResponse>>>
{
    private readonly IWorkModeRepository _workModes;
    private readonly ICurrentUser _currentUser;

    public ListOnboardingWorkModesQueryHandler(IWorkModeRepository workModes, ICurrentUser currentUser)
    {
        _workModes = workModes;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<OnboardingWorkModeResponse>>> Handle(
        ListOnboardingWorkModesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<OnboardingWorkModeResponse>>.Forbidden("Authentication required.");

        var items = await _workModes.ListByLegalEntityAsync(
            _currentUser.TenantId, request.LegalEntityId, includeInactive: false, ct);

        return Result<IReadOnlyList<OnboardingWorkModeResponse>>.Success(
            items.Select(w => new OnboardingWorkModeResponse(w.Id, w.Name)).ToList());
    }
}
