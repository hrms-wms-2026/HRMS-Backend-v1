using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingFinalize;

public class RequestBulkOnboardingFinalizeCommandHandler
    : IRequestHandler<RequestBulkOnboardingFinalizeCommand, Result<BulkOnboardingBatchResponse>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly ICurrentUser _currentUser;

    public RequestBulkOnboardingFinalizeCommandHandler(IBulkOnboardingBatchRepository batchRepository, ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<BulkOnboardingBatchResponse>> Handle(
        RequestBulkOnboardingFinalizeCommand request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetTrackedAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<BulkOnboardingBatchResponse>.NotFound("The batch could not be found.");

        if (batch.Status != BulkOnboardingBatchStatus.DraftsCreated)
            return Result<BulkOnboardingBatchResponse>.Conflict(
                "This batch's drafts must be created before finalizing.");

        if (request.OnboardingDraftIds.Count == 0)
            return Result<BulkOnboardingBatchResponse>.Failure("Select at least one draft to finalize.");

        batch.SelectedDraftIdsJson = JsonSerializer.Serialize(request.OnboardingDraftIds);
        batch.Status = BulkOnboardingBatchStatus.FinalizePending;
        await _batchRepository.SaveChangesAsync(ct);

        return Result<BulkOnboardingBatchResponse>.Success(new BulkOnboardingBatchResponse(
            batch.Id, batch.Status, batch.TotalRows, batch.ValidRows, batch.InvalidRows,
            Array.Empty<string>(), new Dictionary<string, string?>()));
    }
}
