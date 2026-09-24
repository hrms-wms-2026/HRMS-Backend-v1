using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenantSessions;

public sealed record ListTenantSessionsQuery(Guid TenantId)
    : IRequest<Result<IReadOnlyList<TenantSessionResponse>>>;
