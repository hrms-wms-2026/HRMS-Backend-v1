using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Login.Commands.MfaEnable;

public record EnableMfaCommand : IRequest<Result<MfaSetupDto>>;
