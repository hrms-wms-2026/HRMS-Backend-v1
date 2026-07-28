using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;


namespace ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationGoogle;

public sealed record AcceptInvitationGoogleCommand(
    string RawToken,
    string GoogleIdToken,
    IReadOnlyList<LegalAcceptanceItemInput> Acceptances,
    string? IpAddress,
    string? UserAgent) : IRequest<Result<LoginResponseDto>>;
