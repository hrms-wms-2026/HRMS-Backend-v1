using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Commands.BaseLogin;

public sealed record BaseLoginCommand(
    string Email,
    string Password,
    string? IpAddress,
    string? UserAgent) : IRequest<Result<BaseLoginResultDto>>;
