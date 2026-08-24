using MediatR;
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
    bool LocationVerificationRequired,
    int? AllowedRadiusMeters,
    WorkAreaRulesInput WorkAreaRules,
    bool CorrectionRequiresApproval,
    string NotificationRecipientResolver,
    IReadOnlyList<LateDeductionRuleInput> LateDeductionRules,
    bool IsActive) : IRequest<Result<ClockInPolicyResponse>>;
