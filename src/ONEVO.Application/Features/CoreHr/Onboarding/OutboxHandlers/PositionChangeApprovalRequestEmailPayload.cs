namespace ONEVO.Application.Features.CoreHr.Onboarding.OutboxHandlers;

public sealed record PositionChangeApprovalRequestEmailPayload(
    Guid TenantId, Guid ApproverUserId, Guid AccessGrantRequestId,
    string ApproverEmail, string EmployeeName, string PositionName, string? ChangeReason,
    string? TenantSlug = null);
