using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingDraftCreation;

public sealed record RequestBulkOnboardingDraftCreationCommand(Guid BatchId) : IRequest<Result<BulkOnboardingBatchResponse>>;
