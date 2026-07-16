using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Identity;

public sealed class TenantContextAccessor : IWritableTenantContext
{
    private Guid _tenantId = Guid.Empty;
    private string? _slug;
    private TenantStatus? _status;
    private bool _isResolved;
    private TenantContextMode _mode = TenantContextMode.System;

    public Guid TenantId => _tenantId;
    public string? Slug => _slug;
    public TenantStatus? Status => _status;
    public bool IsResolved => _isResolved;
    public TenantContextMode ContextMode => _mode;

    public void Resolve(TenantRegistryEntry tenant)
    {
        _tenantId = tenant.TenantId;
        _slug = tenant.Slug;
        _status = tenant.Status;
        _isResolved = true;
        _mode = TenantContextMode.Tenant;
    }

    public void SetAdminMode()
    {
        _tenantId = Guid.Empty;
        _slug = null;
        _status = null;
        _isResolved = false;
        _mode = TenantContextMode.Admin;
    }

    public void SetSystemMode()
    {
        _tenantId = Guid.Empty;
        _slug = null;
        _status = null;
        _isResolved = false;
        _mode = TenantContextMode.System;
    }
}
