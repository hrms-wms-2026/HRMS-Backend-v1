using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Commands.BaseGoogleLogin;

/// <summary>
/// Base-domain Google login: verify the Google ID token first (identity proof), then use the
/// normalized verified email to find eligible tenant-user candidates via the same allowlisted
/// pre-tenant lookup the password path uses. No password verification applies - Google has
/// already proven identity - so no fixed-work verifier is needed here.
/// </summary>
public record BaseGoogleLoginCommand(
    string GoogleIdToken,
    string? IpAddress,
    string? UserAgent
) : IRequest<Result<BaseLoginResultDto>>;
