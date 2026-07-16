using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Login.Commands.ForcePasswordChange;

public record ForcePasswordChangeCommand(
    string Email,
    string CurrentPassword,
    string NewPassword,
    string? IpAddress,
    string? UserAgent
) : IRequest<Result<LoginResponseDto>>;
