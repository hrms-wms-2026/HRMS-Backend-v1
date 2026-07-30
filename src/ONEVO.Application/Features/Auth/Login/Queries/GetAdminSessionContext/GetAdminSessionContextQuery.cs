using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Queries.GetAdminSessionContext;

public sealed record GetAdminSessionContextQuery
    : IRequest<Result<AdminSessionResponseDto>>;
