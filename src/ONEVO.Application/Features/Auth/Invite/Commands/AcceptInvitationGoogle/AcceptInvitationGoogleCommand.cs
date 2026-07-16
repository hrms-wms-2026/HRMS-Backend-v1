using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationGoogle;

public sealed record AcceptInvitationGoogleCommand(
    string RawToken,
    string GoogleIdToken,
    string? IpAddress,
    string? UserAgent) : IRequest<Result<LoginResponseDto>>;
