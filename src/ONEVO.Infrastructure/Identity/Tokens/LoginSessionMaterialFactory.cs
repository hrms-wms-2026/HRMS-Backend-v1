using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Common.Models;

using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Infrastructure.Identity.Tokens;

/// <summary>
/// Single chokepoint for preparing a tenant web session. Fixed architecture decision: one opaque
/// HttpOnly onevo_session cookie with sliding expiry, no access/refresh/JWT tokens returned to the
/// browser. This class no longer persists the session directly — TenantDatabaseTicketStore.StoreAsync
/// does that when the controller calls HttpContext.SignInAsync("TenantScheme", ...). This class only
/// resolves permissions/entitlements and mints the raw CSRF token + its hash for the controller to
/// stash on AuthenticationProperties.Items before signing in.
/// </summary>
public class LoginSessionMaterialFactory : ILoginSessionMaterialFactory
{
    private readonly ISecureTokenGenerator _tokenService;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IModuleEntitlementService _entitlements;
    private readonly IDateTimeProvider _clock;

    public LoginSessionMaterialFactory(
        ISecureTokenGenerator tokenService,
        IPermissionResolver permissionResolver,
        IModuleEntitlementService entitlements,
        IDateTimeProvider clock)
    {
        _tokenService = tokenService;
        _permissionResolver = permissionResolver;
        _entitlements = entitlements;
        _clock = clock;
    }

    public async Task<Result<LoginResponseDto>> PrepareAsync(
        User user,
        Tenant tenant,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        var permissions = await _permissionResolver.ResolveAsync(user.Id, user.TenantId, null, ct);
        var activeModules = await _entitlements.GetActiveModuleKeysForTenantAsync(user.TenantId, ct);

        var rawCsrfToken = _tokenService.GenerateCsrfToken();
        var csrfTokenHash = _tokenService.HashToken(rawCsrfToken);

        return Result<LoginResponseDto>.Success(new LoginResponseDto(
            CsrfToken: rawCsrfToken,
            CsrfTokenHash: csrfTokenHash,
            ExpiresAt: _clock.UtcNow.Add(SessionPolicy.SlidingWindow),
            User: new CurrentUserDto(user.Id, user.TenantId, user.Email),
            Permissions: permissions,
            ActiveModules: activeModules,
            Workspace: new WorkspaceResponseDto(tenant.Slug, tenant.Name)
        ));
    }
}
