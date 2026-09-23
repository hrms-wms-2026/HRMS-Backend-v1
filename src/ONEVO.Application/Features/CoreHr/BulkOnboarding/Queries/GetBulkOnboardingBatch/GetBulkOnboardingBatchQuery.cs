using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingBatch;

public sealed record GetBulkOnboardingBatchQuery(Guid BatchId) : IRequest<Result<BulkOnboardingBatchDetailResponse>>;

public sealed record BulkOnboardingBatchRowDetailResponse(
    int RowNumber, string Status, string? ErrorMessage, Guid? OnboardingDraftId);

public sealed record BulkOnboardingBatchDetailResponse(
    Guid Id, string Status, int TotalRows, int? ValidRows, int? InvalidRows,
    IReadOnlyList<BulkOnboardingBatchRowDetailResponse> Rows);
