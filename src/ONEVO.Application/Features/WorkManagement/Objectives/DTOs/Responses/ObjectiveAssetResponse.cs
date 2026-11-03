namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

/// <summary>A file attached to a Module. Id is the attachment (entity_assets row) id, which the
/// delete and download endpoints take. DownloadUrl is an API path, so every download re-checks access.</summary>
public sealed record ObjectiveAssetResponse(
    Guid Id, string FileName, long SizeBytes, string ContentType, DateTimeOffset UploadedAt, string DownloadUrl)
{
    public static string DownloadPath(Guid objectiveId, Guid assetId) =>
        $"/api/v1/work/objectives/{objectiveId}/assets/{assetId}/file";
}
