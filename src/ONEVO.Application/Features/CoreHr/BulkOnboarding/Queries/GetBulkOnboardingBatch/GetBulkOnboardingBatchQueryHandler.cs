using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingBatch;

public class GetBulkOnboardingBatchQueryHandler
    : IRequestHandler<GetBulkOnboardingBatchQuery, Result<BulkOnboardingBatchDetailResponse>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly ICurrentUser _currentUser;

    public GetBulkOnboardingBatchQueryHandler(IBulkOnboardingBatchRepository batchRepository, ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<BulkOnboardingBatchDetailResponse>> Handle(GetBulkOnboardingBatchQuery request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<BulkOnboardingBatchDetailResponse>.NotFound("The batch could not be found.");

        var rows = await _batchRepository.ListRowsAsync(_currentUser.TenantId, batch.Id, ct);

        return Result<BulkOnboardingBatchDetailResponse>.Success(new BulkOnboardingBatchDetailResponse(
            batch.Id, batch.Status, batch.TotalRows, batch.ValidRows, batch.InvalidRows,
            rows.Select(r => new BulkOnboardingBatchRowDetailResponse(r.RowNumber, r.Status, r.ErrorMessage, r.OnboardingDraftId)).ToList()));
    }
}
