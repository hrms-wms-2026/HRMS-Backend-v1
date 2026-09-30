using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.EnrollFacePhotos;

public record FaceSetupPhoto(Stream Content, string ContentType, long FileSizeBytes);

/// <summary>
/// Tray face setup: the employee's look-straight, turned-left and turned-right photos, saved
/// together as their reference faces, replacing any previously enrolled face.
/// </summary>
public record EnrollFacePhotosCommand(
    FaceSetupPhoto Front,
    FaceSetupPhoto Left,
    FaceSetupPhoto Right
) : IRequest<Result<FaceEnrollmentResponseDto>>;
