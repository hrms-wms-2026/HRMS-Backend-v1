using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class SubscriptionBillingArchitectureTests
{
    [Fact]
    public void SubscriptionInvoice_IsTenantOwned_ForIsolationFilters()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(SubscriptionInvoice)));
    }

    [Fact]
    public void BillingAuditLogRepository_IsAppendOnly()
    {
        var methods = typeof(IBillingAuditLogRepository)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["AddAsync", "ListByInvoiceAsync", "ListByTenantAsync"], methods);
    }

    [Fact]
    public void BillingAuditLog_HasNoUpdatedAtProperty()
    {
        var propertyNames = typeof(BillingAuditLog)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("UpdatedAt", propertyNames);
    }

    [Fact]
    public void Model_SubscriptionInvoices_RequiredIndexesExist()
    {
        using var context = CreateModelInspectionContext();

        var entityType = context.Model.GetEntityTypes().Single(e => e.ClrType == typeof(SubscriptionInvoice));
        var indexNames = entityType.GetIndexes()
            .Select(i => i.GetDatabaseName())
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ix_subscription_invoices_invoice_number", indexNames);
        Assert.Contains("ix_subscription_invoices_external_invoice_id", indexNames);
        Assert.Contains("ix_subscription_invoices_tenant_id_status", indexNames);
        Assert.Contains("ix_subscription_invoices_tenant_id_due_at", indexNames);

        var invoiceNumberIndex = entityType.GetIndexes()
            .Single(i => i.GetDatabaseName() == "ix_subscription_invoices_invoice_number");
        Assert.True(invoiceNumberIndex.IsUnique);

        var externalInvoiceIndex = entityType.GetIndexes()
            .Single(i => i.GetDatabaseName() == "ix_subscription_invoices_external_invoice_id");
        Assert.True(externalInvoiceIndex.IsUnique);
        Assert.Equal("external_invoice_id IS NOT NULL", externalInvoiceIndex.GetFilter());
    }

    [Fact]
    public void Model_BillingAuditLogs_RequiredIndexesExist()
    {
        using var context = CreateModelInspectionContext();

        var entityType = context.Model.GetEntityTypes().Single(e => e.ClrType == typeof(BillingAuditLog));
        var indexNames = entityType.GetIndexes()
            .Select(i => i.GetDatabaseName())
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ix_billing_audit_logs_invoice_id_created_at", indexNames);
        Assert.Contains("ix_billing_audit_logs_tenant_id_created_at", indexNames);
    }

    [Fact]
    public void AddSubscriptionInvoicesAndBillingAuditLogsMigration_CreatesBothTables()
    {
        var source = ReadMigrationSource("*AddSubscriptionInvoicesAndBillingAuditLogs.cs");

        Assert.Contains("name: \"subscription_invoices\"", source);
        Assert.Contains("name: \"billing_audit_logs\"", source);
        Assert.Contains("ix_subscription_invoices_invoice_number", source);
        Assert.Contains("ix_subscription_invoices_external_invoice_id", source);
        Assert.Contains("ix_subscription_invoices_tenant_id_status", source);
        Assert.Contains("ix_subscription_invoices_tenant_id_due_at", source);
        Assert.Contains("ix_billing_audit_logs_invoice_id_created_at", source);
        Assert.Contains("ix_billing_audit_logs_tenant_id_created_at", source);
    }

    private static string ReadMigrationSource(string pattern)
    {
        var migrationsDir = FindMigrationsDirectory();
        var migrationFiles = Directory.GetFiles(migrationsDir, pattern)
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(migrationFiles);
        return File.ReadAllText(migrationFiles[0]);
    }

    private static string FindMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ONEVO.Infrastructure", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/ONEVO.Infrastructure/Migrations above " + AppContext.BaseDirectory);
    }

    private static ApplicationDbContext CreateModelInspectionContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"arch-test-{Guid.NewGuid()}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }
}
