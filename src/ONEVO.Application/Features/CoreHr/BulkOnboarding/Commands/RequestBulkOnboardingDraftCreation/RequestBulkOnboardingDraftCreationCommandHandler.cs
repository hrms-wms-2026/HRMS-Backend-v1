using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingDraftCreation;

public class RequestBulkOnboardingDraftCreationCommandHandler
    : IRequestHandler<RequestBulkOnboardingDraftCreationCommand, Result<BulkOnboardingBatchResponse>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly ICurrentUser _currentUser;

    public RequestBulkOnboardingDraftCreationCommandHandler(IBulkOnboardingBatchRepository batchRepository, ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<BulkOnboardingBatchResponse>> Handle(
        RequestBulkOnboardingDraftCreationCommand request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetTrackedAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<BulkOnboardingBatchResponse>.NotFound("The batch could not be found.");

        if (batch.Status != BulkOnboardingBatchStatus.Validated)
            return Result<BulkOnboardingBatchResponse>.Conflict(
                "This batch must be validated before drafts can be created.");

        batch.Status = BulkOnboardingBatchStatus.DraftCreationPending;
        await _batchRepository.SaveChangesAsync(ct);

        return Result<BulkOnboardingBatchResponse>.Success(new BulkOnboardingBatchResponse(
            batch.Id, batch.Status, batch.TotalRows, batch.ValidRows, batch.InvalidRows,
            Array.Empty<string>(), new Dictionary<string, string?>()));
    }
}
