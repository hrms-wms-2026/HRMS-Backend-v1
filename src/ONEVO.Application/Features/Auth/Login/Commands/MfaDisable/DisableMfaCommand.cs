using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaDisable;

public record DisableMfaCommand(string CurrentPassword) : IRequest<Result>;
