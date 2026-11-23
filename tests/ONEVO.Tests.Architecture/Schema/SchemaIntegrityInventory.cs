using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Architecture.Schema;

/// <summary>
/// Reads the EF model (no database needed) and lists the integrity gaps of task 39: GUID id columns without
/// a foreign key, and status or type columns without a check constraint. Set ONEVO_SCHEMA_INVENTORY_DIR to
/// a folder to write the full lists there (see SchemaIntegrityInventoryTests).
/// </summary>
internal static class SchemaIntegrityInventory
{
    private static readonly Regex StatusLikeColumn = new("(status|type|kind|state)$", RegexOptions.Compiled);

    public static IModel Model()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=model_only")
            .UseSnakeCaseNamingConvention()
            .Options;
        var dateTimeProvider = new SystemDateTimeProvider();
        using var context = new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
        return context.GetService<IDesignTimeModel>().Model;
    }

    /// <summary>table.column for every uniqueidentifier column ending in _id (not id) with no foreign key.</summary>
    public static SortedSet<string> IdColumnsWithoutForeignKey(IModel model)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (entity, table) in Tables(model))
        {
            var withKey = entity.GetForeignKeys()
                .SelectMany(foreignKey => foreignKey.Properties)
                .Select(property => property.GetColumnName(table))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(table);
                if (column is null || column == "id" || !column.EndsWith("_id", StringComparison.Ordinal))
                    continue;
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) != typeof(Guid))
                    continue;
                if (!withKey.Contains(column))
                    result.Add($"{table.Name}.{column}");
            }
        }

        return result;
    }

    /// <summary>table.column for every text column named like status/type/kind/state that no check constraint mentions.</summary>
    public static SortedSet<string> StatusColumnsWithoutCheck(IModel model)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (entity, table) in Tables(model))
        {
            var checks = entity.GetCheckConstraints().Select(check => check.Sql).ToList();
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(table);
                if (column is null || !StatusLikeColumn.IsMatch(column))
                    continue;
                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clrType != typeof(string) && !clrType.IsEnum)
                    continue;
                if (clrType.IsEnum && property.GetProviderClrType() != typeof(string))
                    continue;
                if (!checks.Any(sql => Regex.IsMatch(sql, $@"\b{Regex.Escape(column)}\b")))
                    result.Add($"{table.Name}.{column}");
            }
        }

        return result;
    }

    private static IEnumerable<(IEntityType Entity, StoreObjectIdentifier Table)> Tables(IModel model) =>
        model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null && !entity.IsOwned())
            .Select(entity => (entity, StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema())));
}
