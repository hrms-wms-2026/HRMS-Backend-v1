using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Login.Commands.BaseForgotPassword;

public record BaseForgotPasswordCommand(string Email) : IRequest<Result>;
