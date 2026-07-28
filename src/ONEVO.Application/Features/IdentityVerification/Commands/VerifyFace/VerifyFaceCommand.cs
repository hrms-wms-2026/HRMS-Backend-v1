using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.IdentityVerification.Commands.VerifyFace;

public sealed record VerifyFaceCommand(
    Guid AgentId,
    string Trigger,
    string FileName,
    string ContentType,
    Stream Content)
    : IRequest<Result<VerifyFaceResponse>>;

public sealed record VerifyFaceResponse(
    Guid VerificationRecordId,
    string Status,
    decimal? MatchConfidence,
    DateTimeOffset VerifiedAt);

