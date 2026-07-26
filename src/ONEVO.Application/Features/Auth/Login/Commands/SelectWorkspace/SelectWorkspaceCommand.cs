using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Commands.SelectWorkspace;

public sealed record SelectWorkspaceCommand(
    string LoginChallenge,
    string Workspace,
    string? IpAddress,
    string? UserAgent) : IRequest<Result<LoginResponseDto>>;
