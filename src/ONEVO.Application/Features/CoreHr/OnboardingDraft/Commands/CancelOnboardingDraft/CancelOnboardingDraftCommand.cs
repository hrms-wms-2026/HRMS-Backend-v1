using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.CancelOnboardingDraft;

public sealed record CancelOnboardingDraftCommand(Guid DraftId) : IRequest<Result>;

public sealed class CancelOnboardingDraftCommandHandler(
    IOnboardingDraftRepository draftRepository, ICurrentUser currentUser, IDateTimeProvider clock)
    : IRequestHandler<CancelOnboardingDraftCommand, Result>
{
    public async Task<Result> Handle(CancelOnboardingDraftCommand request, CancellationToken ct)
    {
        var draft = await draftRepository.GetTrackedAsync(currentUser.TenantId, request.DraftId, ct);
        if (draft is null)
            return Result.NotFound("The draft could not be found.");
        if (draft.StartedById != currentUser.UserId && !currentUser.HasPermission("employees:write"))
            return Result.Forbidden();
        if (draft.Status == OnboardingDraftStatus.Finalized)
            return Result.Conflict("This draft has already been finalized.");
        if (draft.Status == OnboardingDraftStatus.Cancelled)
            return Result.Success();

        draft.Status = OnboardingDraftStatus.Cancelled;
        draft.UpdatedAt = clock.UtcNow;
        await draftRepository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
