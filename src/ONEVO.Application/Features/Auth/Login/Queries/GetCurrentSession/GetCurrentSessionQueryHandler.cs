using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Queries.GetCurrentSession;

public sealed class GetCurrentSessionQueryHandler
    : IRequestHandler<GetCurrentSessionQuery, Result<AuthSessionResponseDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IModuleEntitlementService _entitlements;

    public GetCurrentSessionQueryHandler(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IModuleEntitlementService entitlements)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _entitlements = entitlements;
    }

    public async Task<Result<AuthSessionResponseDto>> Handle(
        GetCurrentSessionQuery request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated
            || !_tenantContext.IsResolved
            || _tenantContext.ContextMode != TenantContextMode.Tenant
            || _currentUser.TenantId != _tenantContext.TenantId)
        {
            return Result<AuthSessionResponseDto>.Failure("Authentication required.", 401);
        }

        var activeModules = await _entitlements.GetActiveModuleKeysForTenantAsync(
            _tenantContext.TenantId,
            ct);

        var response = new AuthSessionResponseDto(
            Authenticated: true,
            User: new CurrentUserDto(
                _currentUser.UserId,
                _currentUser.TenantId,
                _currentUser.Email),
            Permissions: _currentUser.Permissions,
            ActiveModules: activeModules,
            MustChangePassword: false,
            MfaRequired: false,
            ExpiresAt: _currentUser.SessionExpiresAt);

        return Result<AuthSessionResponseDto>.Success(response);
    }
}
