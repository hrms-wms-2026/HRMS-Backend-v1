using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.GetEligibleNominees;

public sealed class GetEligibleNomineesQueryHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees)
    : IRequestHandler<GetEligibleNomineesQuery, Result<GetEligibleNomineesResponse>>
{
    public async Task<Result<GetEligibleNomineesResponse>> Handle(GetEligibleNomineesQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<GetEligibleNomineesResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result<GetEligibleNomineesResponse>.Forbidden("No employee record for the current user.");

        var participant = await events.GetTrackedParticipantAsync(tenantId, request.EventId, employee.Id, ct);
        if (participant is null)
            return Result<GetEligibleNomineesResponse>.NotFound("You are not a participant on this event.");

        var coParticipants = await events.GetParticipantsForEventsAsync(tenantId, [request.EventId], ct);
        if (!coParticipants.TryGetValue(request.EventId, out var list))
            return Result<GetEligibleNomineesResponse>.Success(new GetEligibleNomineesResponse([]));

        var nominees = new List<EligibleNominee>();
        foreach (var other in list.Where(p => p.EmployeeId != employee.Id))
        {
            var otherEmployee = await employees.GetByIdAsync(tenantId, other.EmployeeId, ct);
            if (otherEmployee is null) continue;
            nominees.Add(new EligibleNominee(otherEmployee.Id, $"{otherEmployee.FirstName} {otherEmployee.LastName}"));
        }

        return Result<GetEligibleNomineesResponse>.Success(new GetEligibleNomineesResponse(nominees));
    }
}
