using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.ListOnboardingDrafts;

public record ListOnboardingDraftsQuery(int Page = 1, int PageSize = 25) : IRequest<Result<DraftListPageResponse>>;
