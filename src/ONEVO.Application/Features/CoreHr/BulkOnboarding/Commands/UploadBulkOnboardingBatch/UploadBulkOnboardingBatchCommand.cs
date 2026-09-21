using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;

public sealed record UploadBulkOnboardingBatchCommand(
    string OriginalFileName,
    byte[] FileContent,
    Guid LegalEntityId,
    Guid? DefaultWorkModeId,
    string? DefaultEmploymentType,
    Guid? DefaultChecklistTemplateId) : IRequest<Result<BulkOnboardingBatchResponse>>;
