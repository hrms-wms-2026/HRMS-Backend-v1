using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Features.TimeAttendance.Validation;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.CreateClockInPolicy;

public record CreateClockInPolicyCommand(
    Guid LegalEntityId,
    string Name,
    ClockInPolicyScopeInput Scope,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool CorrectionRequiresApproval,
    string NotificationRecipientResolver,
    IReadOnlyList<LateDeductionRuleInput> LateDeductionRules,
    bool IsActive)
    : IRequest<Result<ClockInPolicyResponse>>, IClockInPolicyInput, IAuditedCommand<Result<ClockInPolicyResponse>>
{
    public AuditEntry DescribeAudit(Result<ClockInPolicyResponse>? response)
        => AuditEntry.For("clock_in_policy.created", "clock_in_policy", response?.Value?.Id,
            details: new { legalEntityId = LegalEntityId, isActive = IsActive });
}
