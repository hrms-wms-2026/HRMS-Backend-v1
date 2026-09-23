using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.FinalizeOnboardingDraft;

public class FinalizeOnboardingDraftCommandHandler : IRequestHandler<FinalizeOnboardingDraftCommand, Result<FinalizeOnboardingDraftResponse>>
{
    private readonly IOnboardingDraftWriteService _writeService;
    private readonly ICurrentUser _currentUser;

    public FinalizeOnboardingDraftCommandHandler(IOnboardingDraftWriteService writeService, ICurrentUser currentUser)
    {
        _writeService = writeService;
        _currentUser = currentUser;
    }

    public Task<Result<FinalizeOnboardingDraftResponse>> Handle(FinalizeOnboardingDraftCommand request, CancellationToken ct) =>
        _writeService.FinalizeAsync(_currentUser.TenantId, _currentUser.UserId, request.DraftId, ct);
}
