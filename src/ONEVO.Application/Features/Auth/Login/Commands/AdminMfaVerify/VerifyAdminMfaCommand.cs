using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminMfaVerify;

/// <summary>Login step 2 — completes an MFA-gated admin login. Challenge comes from the
/// HttpOnly admin_mfa cookie, never from the request body.</summary>
public sealed record VerifyAdminMfaCommand(string Challenge, string Code)
    : IRequest<Result<AdminLoginResultDto>>;
