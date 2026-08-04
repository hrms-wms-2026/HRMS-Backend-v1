namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

public record FaceScanUploadResponseDto(
    Guid FaceScanId,
    string Status,
    long FileSizeBytes);
