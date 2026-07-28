using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminMfaConfirmSetup;

/// <summary>
/// Confirms a freshly generated TOTP secret (from EnableAdminMfaCommand) by proving the caller can
/// produce a valid code — this is what flips PlatformUser.MfaStatus from NotEnrolled to Enrolled.
/// Deliberately separate from VerifyAdminMfaCommand: this runs on an already-authenticated admin
/// session (no login challenge involved), while VerifyAdminMfaCommand runs pre-authentication
/// during login step 2, bound to a short-lived challenge cookie.
/// </summary>
public sealed record ConfirmAdminMfaSetupCommand(string Code) : IRequest<Result>;
