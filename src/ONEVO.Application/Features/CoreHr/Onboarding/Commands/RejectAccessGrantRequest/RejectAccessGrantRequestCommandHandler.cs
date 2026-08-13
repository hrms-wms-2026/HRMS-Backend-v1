using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.RejectAccessGrantRequest;

/// <summary>
/// Rejects a pending <see cref="ONEVO.Domain.Features.CoreHr.Entities.AccessGrantRequest"/>.
/// Creates nothing - no user, employee, invitation, checklist, or role - and does not delete the
/// request or the draft.
///
/// Draft recovery: when the draft is still WaitingForPositionApproval (the normal case - see the
/// defensive guard below), it moves back to Status = Draft / DraftReason =
/// PositionApprovalRejected instead of staying stuck. This matches the userflow doc's "Sensitive
/// Position Approval" rejection rule ("Starter can edit draft, cancel draft, or request again")
/// and, together with FinalizeOnboardingDraftCommandHandler's AnyPendingByDraftAsync check,
/// closes the retry loop: HR can edit the draft (or leave it unchanged) and finalize again, which
/// re-evaluates the position/template and submits a fresh AccessGrantRequest rather than 409ing
/// forever. The rejected request row itself is never deleted or reused - it stays in history and
/// finalize's own GetPendingByDraftAsync only ever matches Pending rows, so it is never treated
/// as duplicate/live state.
///
/// Defensive guard: draft.Status/DraftReason are only rewritten when the draft is currently
/// WaitingForPositionApproval. If a stale Pending request is rejected against a draft that has
/// since moved on (e.g. already Finalized via a different request, or Cancelled), resetting it to
/// Draft would silently undo that outcome - so in that case only draft.UpdatedAt changes.
///
/// Concurrency: still saves through IOnboardingDraftRepository.SaveChangesAsync (touching
/// draft.UpdatedAt even when Status/DraftReason are left alone) rather than the
/// access-grant-request repository's own SaveChangesAsync, specifically so this handler contends
/// on the draft's xmin token with ApproveAccessGrantRequestCommandHandler. AccessGrantRequest
/// itself carries no concurrency token, so without this the loser of a simultaneous approve/reject
/// race would silently overwrite the winner's decision instead of getting a clean 409.
/// </summary>
public class RejectAccessGrantRequestCommandHandler
    : IRequestHandler<RejectAccessGrantRequestCommand, Result<RejectAccessGrantRequestResponse>>
{
    private readonly IAccessGrantRequestRepository _accessGrantRequestRepository;
    private readonly IOnboardingDraftRepository _draftRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RejectAccessGrantRequestCommandHandler(
        IAccessGrantRequestRepository accessGrantRequestRepository,
        IOnboardingDraftRepository draftRepository,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _accessGrantRequestRepository = accessGrantRequestRepository;
        _draftRepository = draftRepository;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<RejectAccessGrantRequestResponse>> Handle(
        RejectAccessGrantRequestCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var grantRequest = await _accessGrantRequestRepository.GetTrackedByIdAsync(tenantId, request.AccessGrantRequestId, ct);
        if (grantRequest is null)
            return Result<RejectAccessGrantRequestResponse>.NotFound("The access grant request could not be found.");

        if (grantRequest.ApprovalStatus != "Pending")
            return Result<RejectAccessGrantRequestResponse>.Conflict("This access grant request has already been decided.");

        if (request.DecisionNote is { Length: > 500 })
            return Result<RejectAccessGrantRequestResponse>.UnprocessableEntity("Decision note must be 500 characters or fewer.");

        if (grantRequest.OnboardingDraftId is null)
            return Result<RejectAccessGrantRequestResponse>.UnprocessableEntity(
                "This access grant request is not correlated to an onboarding draft.");

        var draft = await _draftRepository.GetTrackedAsync(tenantId, grantRequest.OnboardingDraftId.Value, ct);
        if (draft is null)
            return Result<RejectAccessGrantRequestResponse>.NotFound("The related onboarding draft could not be found.");

        var note = request.DecisionNote?.Trim();
        grantRequest.ApprovalStatus = "Rejected";
        grantRequest.DecidedByUserId = _currentUser.UserId;
        grantRequest.DecidedAt = _clock.UtcNow;
        grantRequest.DecisionNote = string.IsNullOrEmpty(note) ? null : note;

        // Only recover the draft when it is genuinely the one waiting on this decision - see the
        // "Defensive guard" section of the class doc comment.
        if (draft.Status == OnboardingDraftStatus.WaitingForPositionApproval)
        {
            draft.Status = OnboardingDraftStatus.Draft;
            draft.DraftReason = OnboardingDraftReason.PositionApprovalRejected;
        }
        draft.UpdatedAt = _clock.UtcNow;

        var saveResult = await SaveAsync(ct);
        if (saveResult is not null)
            return saveResult;

        return Result<RejectAccessGrantRequestResponse>.Success(new RejectAccessGrantRequestResponse(
            grantRequest.Id, draft.Id, grantRequest.ApprovalStatus, draft.Status, draft.DraftReason,
            MessageKey: "onboarding.access_grant.rejected"));
    }

    private async Task<Result<RejectAccessGrantRequestResponse>?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await _draftRepository.SaveChangesAsync(ct);
            return null;
        }
        catch (ConcurrencyConflictException)
        {
            return Result<RejectAccessGrantRequestResponse>.Conflict(
                "This request was just updated by someone else. Please refresh and try again.");
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<RejectAccessGrantRequestResponse>.Conflict(
                "This request conflicts with an existing record. Please refresh and try again.");
        }
    }
}
