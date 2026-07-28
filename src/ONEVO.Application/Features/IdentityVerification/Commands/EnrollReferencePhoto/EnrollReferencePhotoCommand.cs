using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.IdentityVerification.Commands.EnrollReferencePhoto;

public sealed record EnrollReferencePhotoCommand(
    Guid AgentId,
    string NoticeVersion,
    string FileName,
    string ContentType,
    Stream Content)
    : IRequest<Result<EnrollReferencePhotoResponse>>;

public sealed record EnrollReferencePhotoResponse(
    Guid ReferencePhotoId,
    string Status,
    bool IsActive,
    DateTimeOffset CapturedAt);

