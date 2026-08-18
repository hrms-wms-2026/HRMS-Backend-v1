using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;

public sealed record ValidateBulkOnboardingBatchCommand(
    Guid BatchId, IReadOnlyDictionary<string, string?> Mapping) : IRequest<Result<ValidateBulkOnboardingBatchResult>>;

public sealed record ValidateBulkOnboardingBatchResult(
    int ValidRows, int InvalidRows, IReadOnlyList<RowValidationResultItem> Rows);

public sealed record RowValidationResultItem(int RowNumber, string Status, string? ErrorMessage);
