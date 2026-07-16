using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Login.Commands.ResetPassword;

public record ResetPasswordCommand(string Token, string NewPassword) : IRequest<Result>;
