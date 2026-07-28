using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminMfaEnable;

public sealed record EnableAdminMfaCommand : IRequest<Result<MfaSetupDto>>;
