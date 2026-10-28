namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveAssetViewModel(
    Guid Id, string FileName, long SizeBytes, string ContentType, DateTimeOffset UploadedAt, string DownloadUrl);
