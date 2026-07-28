using System.Data;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Identity.Tenancy;

public sealed class TenantContextSwitcher : ITenantContextSwitcher
{
    private readonly ApplicationDbContext _db;
    private readonly IWritableTenantContext _writableTenantContext;

    public TenantContextSwitcher(ApplicationDbContext db, IWritableTenantContext writableTenantContext)
    {
        _db = db;
        _writableTenantContext = writableTenantContext;
    }

    public async Task SwitchToTenantAsync(TenantRegistryEntry tenant, CancellationToken ct = default)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State == ConnectionState.Open)
        {
            await _db.Database.CloseConnectionAsync();
        }

        _writableTenantContext.Resolve(tenant);
    }
}
