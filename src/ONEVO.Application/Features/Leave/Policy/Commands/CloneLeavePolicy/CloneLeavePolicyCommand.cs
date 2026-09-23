using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;

public record CloneLeavePolicyCommand(
    Guid SourcePolicyId,
    string Name,
    string Country,
    IReadOnlyList<Guid> LegalEntityIds,
    DateOnly EffectiveFrom,
    bool ConfirmReplaceExistingLegalEntityAssignments) : IRequest<Result<LeavePolicyResponse>>;
