using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.DTOs.Responses;
using ONEVO.Application.Features.Auth.Invite.Mappers;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.Invite.Queries.GetInvitationByToken;

public sealed class GetInvitationByTokenQueryHandler
    : IRequestHandler<GetInvitationByTokenQuery, Result<InvitationDetailDto>>
{
    private readonly IInvitationTokenRepository _invitations;
    private readonly ITenantRepository _tenants;
    private readonly IRoleRepository _roles;
    private readonly ITenantAuthPolicyRepository _policies;
    private readonly IDateTimeProvider _clock;

    public GetInvitationByTokenQueryHandler(
        IInvitationTokenRepository invitations,
        ITenantRepository tenants,
        IRoleRepository roles,
        ITenantAuthPolicyRepository policies,
        IDateTimeProvider clock)
    {
        _invitations = invitations;
        _tenants = tenants;
        _roles = roles;
        _policies = policies;
        _clock = clock;
    }

    public async Task<Result<InvitationDetailDto>> Handle(
        GetInvitationByTokenQuery request,
        CancellationToken ct)
    {
        var hash = InvitationTokenHasher.Hash(request.RawToken);
        var inv = await _invitations.GetByTokenHashAsync(hash, ct);
        if (inv is null)
            return Result<InvitationDetailDto>.NotFound("Invitation not found.");

        var tenant = await _tenants.GetByIdAsync(inv.TenantId, ct);
        if (tenant is null)
            return Result<InvitationDetailDto>.NotFound("Tenant not found.");

        var role = inv.RoleId.HasValue
            ? await _roles.GetByIdForTenantAsync(inv.TenantId, inv.RoleId.Value, ct)
            : null;
        var policy = await _policies.GetByTenantIdAsync(inv.TenantId, ct);

        return Result<InvitationDetailDto>.Success(
            InviteMapper.ToDto(inv, tenant, role, policy, _clock.UtcNow));
    }
}
