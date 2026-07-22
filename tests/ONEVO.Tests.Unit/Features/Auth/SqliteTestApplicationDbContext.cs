using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Test-only context for running the production model on SQLite. SQLite stores DateTimeOffset as
/// TEXT and cannot translate comparisons on it, so every DateTimeOffset property is remapped to a
/// sortable 64-bit value here. This workaround lives exclusively in the test project: the
/// production ApplicationDbContext is provider-clean and targets PostgreSQL/Npgsql only.
/// </summary>
internal sealed class SqliteTestApplicationDbContext : ApplicationDbContext
{
    public SqliteTestApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        AuditableEntityInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        DomainEventDispatchInterceptor domainEventInterceptor,
        ITenantContext tenantContext)
        : base(options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, tenantContext)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset)
                    || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
                }
            }
        }
    }
}
