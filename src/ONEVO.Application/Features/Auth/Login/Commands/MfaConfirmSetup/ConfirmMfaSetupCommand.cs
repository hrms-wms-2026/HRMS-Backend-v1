using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaConfirmSetup;

public record ConfirmMfaSetupCommand(string Code) : IRequest<Result>;
