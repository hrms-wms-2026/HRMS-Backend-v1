using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Login.Commands.ResetAdminPassword;

public sealed record ResetAdminPasswordCommand(string Token, string NewPassword) : IRequest<Result>;
