using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.GetOnboardingDraft;

public record GetOnboardingDraftQuery(Guid DraftId) : IRequest<Result<OnboardingDraftResponse>>;
