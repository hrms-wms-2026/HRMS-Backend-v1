using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Login.Commands.RequestPasswordReset;

public record RequestPasswordResetCommand(string Email) : IRequest<Result>;
