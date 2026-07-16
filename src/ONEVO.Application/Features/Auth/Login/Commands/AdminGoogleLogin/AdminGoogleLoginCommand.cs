using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminGoogleLogin;

public sealed record AdminGoogleLoginCommand(string GoogleIdToken, string? IpAddress = null, string? UserAgent = null)
    : IRequest<Result<AdminLoginResultDto>>;
