using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Login.Commands.MfaVerify;

public record VerifyMfaCommand(
    string MfaChallenge,
    string Code,
    string? IpAddress,
    string? UserAgent
) : IRequest<Result<LoginResponseDto>>;
