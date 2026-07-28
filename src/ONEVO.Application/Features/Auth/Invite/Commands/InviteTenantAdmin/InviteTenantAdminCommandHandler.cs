using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Invite.Commands.InviteTenantAdmin;

public class InviteTenantAdminCommandHandler
    : IRequestHandler<InviteTenantAdminCommand, Result<TenantInvitationDto>>
{
    private readonly ITenantRepository _tenants;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantOwnerInvitationService _invitationService;

    public InviteTenantAdminCommandHandler(
        ITenantRepository tenants,
        ICurrentUser currentUser,
        ITenantOwnerInvitationService invitationService)
    {
        _tenants = tenants;
        _currentUser = currentUser;
        _invitationService = invitationService;
    }

    public async Task<Result<TenantInvitationDto>> Handle(
        InviteTenantAdminCommand request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TenantInvitationDto>.Forbidden("Authentication required.");

        var tenant = await _tenants.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result<TenantInvitationDto>.NotFound($"Tenant '{request.TenantId}' not found.");

        if (tenant.Status != TenantStatus.Provisioning)
            return Result<TenantInvitationDto>.Conflict(
                "Tenant owner invite can only be sent while the tenant is in provisioning status.");

        var inviteRequest = new TenantOwnerInviteRequest(
            Email: request.Email,
            FirstName: request.FirstName,
            LastName: request.LastName,
            CompletionMethods: request.CompletionMethods,
            AllowGoogleEmailMismatch: request.AllowGoogleEmailMismatch,
            AllowedEmailDomains: request.AllowedEmailDomains);

        return await _invitationService.InviteOwnerAsync(request.TenantId, inviteRequest, ct);
    }
}
