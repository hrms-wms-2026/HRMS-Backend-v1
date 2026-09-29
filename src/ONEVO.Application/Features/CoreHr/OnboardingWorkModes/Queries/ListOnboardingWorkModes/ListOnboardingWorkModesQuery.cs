using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingWorkModes.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingWorkModes.Queries.ListOnboardingWorkModes;

public record ListOnboardingWorkModesQuery(Guid LegalEntityId)
    : IRequest<Result<IReadOnlyList<OnboardingWorkModeResponse>>>;
