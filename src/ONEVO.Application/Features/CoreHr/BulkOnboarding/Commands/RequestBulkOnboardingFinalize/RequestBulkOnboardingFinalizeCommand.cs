using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingFinalize;

public sealed record RequestBulkOnboardingFinalizeCommand(
    Guid BatchId, IReadOnlyList<Guid> OnboardingDraftIds) : IRequest<Result<BulkOnboardingBatchResponse>>;
