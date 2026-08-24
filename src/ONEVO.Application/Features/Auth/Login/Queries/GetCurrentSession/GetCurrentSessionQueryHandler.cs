using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.Login.Queries.GetCurrentSession;

public sealed class GetCurrentSessionQueryHandler
    : IRequestHandler<GetCurrentSessionQuery, Result<AuthSessionResponseDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IModuleEntitlementService _entitlements;
    private readonly ITenantRepository _tenants;
    private readonly IEmployeeRepository _employees;

    public GetCurrentSessionQueryHandler(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IModuleEntitlementService entitlements,
        ITenantRepository tenants,
        IEmployeeRepository employees)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _entitlements = entitlements;
        _tenants = tenants;
        _employees = employees;
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

        // ITenantContext exposes Slug but not a display name, so the tenant repository is the
        // source of truth for the human-readable name here.
        var tenant = await _tenants.GetByIdAsync(_tenantContext.TenantId, ct);
        if (tenant is null)
            return Result<AuthSessionResponseDto>.Failure("Authentication required.", 401);

        var employee = await _employees.GetByUserIdAsync(_tenantContext.TenantId, _currentUser.UserId, ct);

        var response = new AuthSessionResponseDto(
            Authenticated: true,
            User: new CurrentUserDto(
                _currentUser.UserId,
                _currentUser.TenantId,
                _currentUser.Email,
                employee?.Id),
            Permissions: _currentUser.Permissions,
            ActiveModules: activeModules,
            MustChangePassword: false,
            MfaRequired: false,
            ExpiresAt: _currentUser.SessionExpiresAt,
            // Slug comes from the already-resolved host context (ITenantContext), not the fresh
            // repository read - the repository lookup exists only because display name isn't on
            // ITenantContext.
            Workspace: new WorkspaceResponseDto(_tenantContext.Slug!, tenant.Name));

        return Result<AuthSessionResponseDto>.Success(response);
    }
}
