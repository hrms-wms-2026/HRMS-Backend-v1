using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;

namespace ONEVO.Application.Features.Leave.Entitlement.Queries.PreviewGenerateEntitlements;

public class PreviewGenerateEntitlementsQueryHandler
    : IRequestHandler<PreviewGenerateEntitlementsQuery, Result<LeaveEntitlementGenerationPreviewResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly LeaveEntitlementPlanner _planner;

    public PreviewGenerateEntitlementsQueryHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        LeaveEntitlementPlanner planner)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _planner = planner;
    }

    public async Task<Result<LeaveEntitlementGenerationPreviewResponse>> Handle(
        PreviewGenerateEntitlementsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveEntitlementGenerationPreviewResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveEntitlementGenerationPreviewResponse>.Forbidden("Tenant context missing.");

        var asOfDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow.UtcDateTime);
        var plan = await _planner.PlanAsync(_currentUser.TenantId, request.Year, request.LegalEntityId, asOfDate, ct);

        return Result<LeaveEntitlementGenerationPreviewResponse>.Success(new LeaveEntitlementGenerationPreviewResponse(
            plan.Year,
            plan.EmployeeCount,
            plan.Lines.Count,
            plan.Lines,
            plan.Skipped));
    }
}
