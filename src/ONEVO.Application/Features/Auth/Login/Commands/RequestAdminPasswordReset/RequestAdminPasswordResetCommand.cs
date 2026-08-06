using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Login.Commands.RequestAdminPasswordReset;

public sealed record RequestAdminPasswordResetCommand(
    string Email, string? IpAddress, string? UserAgent) : IRequest<Result>;
