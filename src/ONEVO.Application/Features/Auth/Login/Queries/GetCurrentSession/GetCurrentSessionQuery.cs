using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Queries.GetCurrentSession;

public sealed record GetCurrentSessionQuery
    : IRequest<Result<AuthSessionResponseDto>>;
