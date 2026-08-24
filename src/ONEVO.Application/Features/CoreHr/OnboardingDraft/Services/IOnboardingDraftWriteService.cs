using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

/// <summary>
/// The tenant/user-parameterized core of onboarding-draft save/finalize. Exists because
/// SaveOnboardingDraftCommandHandler/FinalizeOnboardingDraftCommandHandler originally read
/// ICurrentUser.TenantId/.UserId directly - which is HttpContext-backed and resolves to
/// Guid.Empty outside an HTTP request. This service takes those two values as explicit
/// parameters so BOTH the existing MediatR handlers (HTTP path, passing ICurrentUser's values)
/// and BulkOnboardingBatchProcessor (background path, passing the batch's stored values) can
/// call the exact same logic. See docs/superpowers/specs/next/2026-08-18-bulk-employee-onboarding-backend-design.md §5.
/// </summary>
public interface IOnboardingDraftWriteService
{
    Task<Result<OnboardingDraftResponse>> SaveAsync(
        Guid tenantId, Guid actingUserId, SaveOnboardingDraftCommand request, CancellationToken ct);

    Task<Result<FinalizeOnboardingDraftResponse>> FinalizeAsync(
        Guid tenantId, Guid actingUserId, Guid draftId, CancellationToken ct);
}
