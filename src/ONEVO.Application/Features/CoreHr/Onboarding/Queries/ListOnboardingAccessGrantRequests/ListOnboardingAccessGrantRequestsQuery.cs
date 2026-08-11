using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListOnboardingAccessGrantRequests;

/// <summary>Position Approver Inbox listing. <paramref name="Status"/> and
/// <paramref name="ActionType"/> are the caller-facing lower-case query values (e.g. "pending",
/// "onboarding") - the handler normalizes them to the stored literals and rejects anything it
/// does not recognize with a 400.</summary>
public sealed record ListOnboardingAccessGrantRequestsQuery(
    string? Status = "pending",
    string? ActionType = "onboarding",
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? LegalEntityId = null,
    Guid? RequestedRoleId = null) : IRequest<Result<OnboardingAccessGrantRequestListPageResponse>>;
