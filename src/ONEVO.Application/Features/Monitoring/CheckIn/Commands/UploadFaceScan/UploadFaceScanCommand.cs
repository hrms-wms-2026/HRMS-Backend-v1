using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;

public record UploadFaceScanCommand(
    Guid CheckInId,
    Stream ImageStream,
    string ContentType,
    long FileSizeBytes
) : IRequest<Result<FaceScanUploadResponseDto>>;
