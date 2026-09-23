using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenantSessions;

public sealed class ListTenantSessionsQueryHandler
    : IRequestHandler<ListTenantSessionsQuery, Result<IReadOnlyList<TenantSessionResponse>>>
{
    private readonly ITenantRepository _tenants;
    private readonly ISessionRepository _sessions;
    private readonly IUserRepository _users;
    private readonly IDateTimeProvider _clock;

    public ListTenantSessionsQueryHandler(
        ITenantRepository tenants,
        ISessionRepository sessions,
        IUserRepository users,
        IDateTimeProvider clock)
    {
        _tenants = tenants;
        _sessions = sessions;
        _users = users;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<TenantSessionResponse>>> Handle(
        ListTenantSessionsQuery request,
        CancellationToken ct)
    {
        if (await _tenants.GetByIdAsync(request.TenantId, ct) is null)
            return Result<IReadOnlyList<TenantSessionResponse>>.NotFound("Tenant not found.");

        var sessions = await _sessions.ListActiveByTenantIdAsync(request.TenantId, _clock.UtcNow, ct);

        var responses = new List<TenantSessionResponse>(sessions.Count);
        foreach (var session in sessions)
        {
            var user = await _users.GetByIdAsync(session.UserId, ct);
            responses.Add(new TenantSessionResponse(
                session.Id,
                session.UserId,
                user?.Email,
                user is null ? null : $"{user.FirstName} {user.LastName}".Trim(),
                session.IpAddress,
                session.UserAgent,
                session.StartedAt,
                session.LastActivityAt,
                session.ExpiresAt));
        }

        return Result<IReadOnlyList<TenantSessionResponse>>.Success(responses);
    }
}
