using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationPassword;

public sealed record AcceptInvitationPasswordCommand(
    string RawToken,
    string Password,
    string? Phone,
    string? IpAddress,
    string? UserAgent) : IRequest<Result<LoginResponseDto>>;
