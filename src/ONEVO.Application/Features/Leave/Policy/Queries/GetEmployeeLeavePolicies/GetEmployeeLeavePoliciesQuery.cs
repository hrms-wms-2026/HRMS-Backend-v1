using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Policy.Queries.GetEmployeeLeavePolicies;

/// <summary>The policy each active leave type resolves to for the employee today, and its source.</summary>
public sealed record GetEmployeeLeavePoliciesQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<EmployeeLeavePolicyRow>>>;

public sealed class GetEmployeeLeavePoliciesQueryHandler
    : IRequestHandler<GetEmployeeLeavePoliciesQuery, Result<IReadOnlyList<EmployeeLeavePolicyRow>>>
{
    private readonly ILeavePolicyResolver _resolver;
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetEmployeeLeavePoliciesQueryHandler(
        ILeavePolicyResolver resolver, ILeaveTypeRepository leaveTypes, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _resolver = resolver;
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<EmployeeLeavePolicyRow>>> Handle(GetEmployeeLeavePoliciesQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var resolved = (await _resolver.ResolveAllAsync(tenantId, request.EmployeeId, _clock.Today, ct))
            .ToDictionary(r => r.LeaveTypeId);
        var types = await _leaveTypes.ListAsync(tenantId, includeInactive: true, ct);

        var rows = types
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => resolved.TryGetValue(t.Id, out var r)
                ? new EmployeeLeavePolicyRow(
                    t.Id, t.Name, t.Color, r.PolicyId, r.PolicyName, r.SourceKind, r.SourceTargetName,
                    r.Version.AmountHours, r.Version.AmountPeriod, r.SourceKind == LeavePolicyTargetKinds.Employee)
                : new EmployeeLeavePolicyRow(t.Id, t.Name, t.Color, null, null, null, null, null, null, false))
            .ToList();

        return Result<IReadOnlyList<EmployeeLeavePolicyRow>>.Success(rows);
    }
}
