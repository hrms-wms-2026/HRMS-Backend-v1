// src/ONEVO.Application/Features/Auth/Commands/AdminLogin/AdminLoginCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminLogin;

public sealed record AdminLoginCommand(string Email, string Password, string? IpAddress = null, string? UserAgent = null)
    : IRequest<Result<AdminLoginResultDto>>;
