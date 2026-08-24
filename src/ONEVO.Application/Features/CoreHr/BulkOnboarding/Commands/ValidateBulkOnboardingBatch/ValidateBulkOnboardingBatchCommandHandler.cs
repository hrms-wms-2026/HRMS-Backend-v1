using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;

public class ValidateBulkOnboardingBatchCommandHandler
    : IRequestHandler<ValidateBulkOnboardingBatchCommand, Result<ValidateBulkOnboardingBatchResult>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly IBulkOnboardingValidationRunner _validationRunner;
    private readonly ICurrentUser _currentUser;

    public ValidateBulkOnboardingBatchCommandHandler(
        IBulkOnboardingBatchRepository batchRepository,
        IBulkOnboardingValidationRunner validationRunner,
        ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _validationRunner = validationRunner;
        _currentUser = currentUser;
    }

    public async Task<Result<ValidateBulkOnboardingBatchResult>> Handle(
        ValidateBulkOnboardingBatchCommand request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetTrackedAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<ValidateBulkOnboardingBatchResult>.NotFound("The batch could not be found.");

        var result = await _validationRunner.RunAsync(batch, request.Mapping, ct);
        await _batchRepository.SaveChangesAsync(ct);
        return Result<ValidateBulkOnboardingBatchResult>.Success(result);
    }
}
