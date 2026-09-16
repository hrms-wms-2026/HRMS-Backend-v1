namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record UploadBulkOnboardingBatchRequest(
    IFormFile File,
    Guid LegalEntityId,
    Guid? DefaultWorkModeId,
    string? DefaultEmploymentType,
    Guid? DefaultChecklistTemplateId);
