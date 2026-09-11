using MediatR;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Application.Common.Behaviors;

public class AdminTenantContextBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentPlatformUserContext _currentPlatformUser;
    private readonly IWritableTenantContext _tenantContext;

    public AdminTenantContextBehavior(
        ICurrentPlatformUserContext currentPlatformUser,
        IWritableTenantContext tenantContext)
    {
        _currentPlatformUser = currentPlatformUser;
        _tenantContext = tenantContext;
    }

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // Every request authenticated via AdminScheme (the Dev Platform admin cookie) is, by
        // definition, a cross-tenant admin operation and must be able to read/write
        // RLS-protected tenant-scoped rows regardless of which tenant they belong to.
        // HostTenantResolutionMiddleware normally derives admin mode from the request's
        // subdomain, but that's host-header-dependent and doesn't fire for every path an
        // admin-authenticated request can take (e.g. a dev proxy that rewrites Host) - set it
        // here directly from the authenticated identity so admin handlers never depend on
        // ambient host state to see their own writes.
        if (_currentPlatformUser.UserId is not null)
        {
            _tenantContext.SetAdminMode();
        }

        return next();
    }
}
