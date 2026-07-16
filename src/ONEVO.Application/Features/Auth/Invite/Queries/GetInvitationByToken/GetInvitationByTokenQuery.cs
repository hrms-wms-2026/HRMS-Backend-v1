using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Invite.DTOs.Responses;


namespace ONEVO.Application.Features.Auth.Invite.Queries.GetInvitationByToken;

public sealed record GetInvitationByTokenQuery(string RawToken)
    : IRequest<Result<InvitationDetailDto>>;
