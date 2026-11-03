using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Services;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Entitlement.Queries.PreviewGenerateEntitlements;

public class PreviewGenerateEntitlementsQueryHandler
    : IRequestHandler<PreviewGenerateEntitlementsQuery, Result<LeaveEntitlementGenerationPreviewResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ILeaveCreditService _credits;

    public PreviewGenerateEntitlementsQueryHandler(
        ICurrentUser currentUser, IDateTimeProvider clock, IEmployeeRepository employees,
        ILeaveTypeRepository leaveTypes, ILeaveCreditService credits)
    {
        _currentUser = currentUser;
        _clock = clock;
        _employees = employees;
        _leaveTypes = leaveTypes;
        _credits = credits;
    }

    public async Task<Result<LeaveEntitlementGenerationPreviewResponse>> Handle(
        PreviewGenerateEntitlementsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveEntitlementGenerationPreviewResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveEntitlementGenerationPreviewResponse>.Forbidden("Tenant context missing.");

        var tenantId = _currentUser.TenantId;
        var employees = await _employees.ListActiveByLegalEntityAsync(tenantId, request.LegalEntityId, ct);
        var outcomes = await _credits.CreditAsync(tenantId, employees.Select(e => e.Id).ToList(), request.Year,
            DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime), true, _currentUser.UserId, ct);
        var types = (await _leaveTypes.ListAsync(tenantId, true, ct)).ToDictionary(t => t.Id);
        var employeeMap = employees.ToDictionary(e => e.Id);
        var lines = outcomes.Where(x => x.SkipReason is null).Select(x =>
        {
            var employee = employeeMap[x.EmployeeId];
            return new LeaveEntitlementGenerationLineResponse(
                x.EmployeeId, employee.EmployeeNumber, $"{employee.FirstName} {employee.LastName}".Trim(),
                x.LeaveTypeId, types.GetValueOrDefault(x.LeaveTypeId)?.Name ?? string.Empty,
                x.ExpectedCreditedHours, 0m, x.ExpectedCreditedHours, false, 0m, null, null);
        }).ToList();
        var skipped = outcomes.Where(x => x.SkipReason is not null).Select(x =>
            new LeaveEntitlementGenerationSkipResponse(x.EmployeeId,
                employeeMap.TryGetValue(x.EmployeeId, out var employee)
                    ? $"{employee.FirstName} {employee.LastName}".Trim() : null,
                x.SkipReason!)).ToList();
        return Result<LeaveEntitlementGenerationPreviewResponse>.Success(new(
            request.Year, employees.Count, lines.Count, lines, skipped));
    }
}
