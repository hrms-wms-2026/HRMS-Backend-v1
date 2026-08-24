using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;

public class SaveOnboardingDraftCommandHandler : IRequestHandler<SaveOnboardingDraftCommand, Result<OnboardingDraftResponse>>
{
    private readonly IOnboardingDraftWriteService _writeService;
    private readonly ICurrentUser _currentUser;

    public SaveOnboardingDraftCommandHandler(IOnboardingDraftWriteService writeService, ICurrentUser currentUser)
    {
        _writeService = writeService;
        _currentUser = currentUser;
    }

    public Task<Result<OnboardingDraftResponse>> Handle(SaveOnboardingDraftCommand request, CancellationToken ct) =>
        _writeService.SaveAsync(_currentUser.TenantId, _currentUser.UserId, request, ct);
}
