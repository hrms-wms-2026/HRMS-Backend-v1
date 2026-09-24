using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.FinalizeOnboardingDraft;

public record FinalizeOnboardingDraftCommand(Guid DraftId) : IRequest<Result<FinalizeOnboardingDraftResponse>>;
