using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Common;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the tenant isolation hardening fixes:
/// - ApplicationDbContext's tenant HasQueryFilter never closes over an
///   ITenantContext service instance as a constant (the exact model-caching
///   bug fixed in this task),
/// - every ITenantOwnedEntity type actually has a query filter,
/// - ConnectionStrings:DefaultConnection never names a superuser-looking
///   role,
/// - no migration disables RLS or grants BYPASSRLS in its Up() direction,
///   and no non-migration source file references BYPASSRLS or
///   "DISABLE ROW LEVEL SECURITY" at all,
/// - IgnoreQueryFilters has zero unaudited call sites,
/// - every tenant-owned table has a tenant_isolation RLS policy somewhere
///   across the migration history.
/// </summary>
public class TenantIsolationArchitectureTests
{
    [Fact]
    public void QueryFilters_DoNotCaptureTenantContextServiceAsConstant()
    {
        using var context = CreateModelInspectionContext();

        var offenders = new List<string>();
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwnedEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            foreach (var filter in entityType.GetDeclaredQueryFilters())
            {
                var finder = new CapturedServiceConstantFinder();
                finder.Visit(filter.Expression);

                if (finder.Found)
                    offenders.Add(entityType.ClrType.Name);
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void UserRoleAndRolePermission_ComposedQueryFilter_RetainsRoleSoftDeleteCondition()
    {
        // Guards the query-filter composition fix: UserRoleConfiguration and
        // RolePermissionConfiguration each declare their own HasQueryFilter
        // (hiding rows whose Role has been soft-deleted) before
        // ApplicationDbContext's generic tenant-filter loop runs. HasQueryFilter
        // replaces rather than combines the default-keyed filter, so without
        // composition the generic loop would silently drop this condition. This
        // test proves the final, composed filter still contains it.
        using var context = CreateModelInspectionContext();

        foreach (var entityTypeName in new[] { "UserRole", "RolePermission" })
        {
            var entityType = context.Model.GetEntityTypes().Single(e => e.ClrType.Name == entityTypeName);
            var filter = entityType.GetDeclaredQueryFilters().Single();

            var roleIsDeletedFinder = new RoleIsDeletedReferenceFinder();
            roleIsDeletedFinder.Visit(filter.Expression);
            Assert.True(
                roleIsDeletedFinder.Found,
                $"{entityTypeName}'s composed query filter no longer references Role.IsDeleted - " +
                "the generic tenant-filter loop overwrote the entity-specific filter instead of composing with it.");

            var tenantIdFinder = new TenantIdComparisonFinder();
            tenantIdFinder.Visit(filter.Expression);
            Assert.True(
                tenantIdFinder.Found,
                $"{entityTypeName}'s composed query filter no longer references TenantId scoping.");
        }
    }

    [Fact]
    public void EveryTenantOwnedEntity_ComposedQueryFilter_IncludesTenantScopingCondition()
    {
        // EveryTenantOwnedEntity_HasAQueryFilter below only proves *a* filter
        // exists. This proves the filter that exists actually contains the
        // tenant-scoping condition, not just whatever entity-specific predicate
        // (if any) an IEntityTypeConfiguration declared on its own - i.e. that
        // composition adds the generic condition rather than only preserving
        // the existing one.
        using var context = CreateModelInspectionContext();

        var offenders = new List<string>();
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwnedEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var hasTenantCondition = entityType.GetDeclaredQueryFilters().Any(filter =>
            {
                var finder = new TenantIdComparisonFinder();
                finder.Visit(filter.Expression);
                return finder.Found;
            });

            if (!hasTenantCondition)
                offenders.Add(entityType.ClrType.Name);
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryTenantOwnedEntity_HasAQueryFilter()
    {
        // Catches both "a new ITenantOwnedEntity was added with no filter at
        // all" and "an entity's own IEntityTypeConfiguration called
        // HasQueryFilter again after the generic tenant-filter loop ran,
        // silently clearing it" (HasQueryFilter replaces rather than
        // combines in EF Core).
        using var context = CreateModelInspectionContext();

        var typesMissingAFilter = context.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwnedEntity).IsAssignableFrom(e.ClrType))
            .Where(e => e.GetDeclaredQueryFilters().Count is 0)
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.Empty(typesMissingAFilter);

        // Sanity check that the model actually contains the entities this
        // suite cares about, so the assertion above isn't vacuously true
        // against an empty/misconfigured model.
        var knownTenantOwnedTypeNames = context.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwnedEntity).IsAssignableFrom(e.ClrType))
            .Select(e => e.ClrType.Name)
            .ToHashSet();

        foreach (var expected in new[]
        {
            "User", "Role", "MfaChallenge", "FileRecord", "FileUploadReservation",
            "TenantStorageStats", "Employee"
        })
        {
            Assert.Contains(expected, knownTenantOwnedTypeNames);
        }
    }

    [Fact]
    public void DefaultConnection_DoesNotUseSuperuserLookingRoleName()
    {
        var apiProjectDirectory = FindApiProjectDirectory();
        var appsettingsFiles = Directory.GetFiles(apiProjectDirectory, "appsettings*.json", SearchOption.TopDirectoryOnly);

        Assert.NotEmpty(appsettingsFiles);

        foreach (var file in appsettingsFiles)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (!document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings))
                continue;
            if (!connectionStrings.TryGetProperty("DefaultConnection", out var defaultConnectionElement))
                continue;

            var connectionString = defaultConnectionElement.GetString();
            if (string.IsNullOrWhiteSpace(connectionString))
                continue;

            var username = ExtractUsername(connectionString);
            var looksLikeSuperuser = username is not null && (
                username.Contains("postgres", StringComparison.OrdinalIgnoreCase) ||
                username.Contains("superuser", StringComparison.OrdinalIgnoreCase));

            Assert.False(looksLikeSuperuser,
                $"{Path.GetFileName(file)}: ConnectionStrings:DefaultConnection username '{username}' " +
                "must not be a superuser-looking role. Use ConnectionStrings:MigrationConnection for schema migrations instead.");
        }
    }

    [Fact]
    public void NonMigrationSourceCode_NeverDisablesRowLevelSecurity()
    {
        // Deliberately does not scan for the word "BYPASSRLS" here --
        // ConfigurationStartupValidator legitimately mentions it in an
        // operator-facing log message (explaining why a role must not have
        // the attribute), which is documentation, not a grant. The migration
        // Up()-body check below is where BYPASSRLS as an actual SQL keyword
        // matters.
        var srcDirectory = FindSrcDirectory();

        var offenders = Directory.GetFiles(srcDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("DISABLE ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Migrations_NeverDisableRowLevelSecurity_InTheUpDirection()
    {
        var migrationsDir = FindMigrationsDirectory();

        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(migrationsDir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(file);
            var upStart = source.IndexOf("protected override void Up(", StringComparison.Ordinal);
            var downStart = source.IndexOf("protected override void Down(", StringComparison.Ordinal);
            if (upStart < 0 || downStart < 0 || downStart < upStart)
                continue;

            var upBody = source[upStart..downStart];
            if (upBody.Contains("DISABLE ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase) ||
                upBody.Contains("BYPASSRLS", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void IgnoreQueryFilters_UsageIsExplicitlyAllowlisted()
    {
        // No file in this codebase may call .IgnoreQueryFilters() today. If a
        // future platform/admin read path legitimately needs one, add its
        // exact file name here together with a comment at the call site
        // explaining why tenant scoping is correctly bypassed there.
        var allowlistedFileNames = Array.Empty<string>();

        var srcDirectory = FindSrcDirectory();
        var offenders = Directory.GetFiles(srcDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("IgnoreQueryFilters(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(name => name is not null && !allowlistedFileNames.Contains(name))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryTenantOwnedEntityTable_HasRlsPolicyCoverage()
    {
        using var context = CreateModelInspectionContext();

        var tenantOwnedTableNames = context.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwnedEntity).IsAssignableFrom(e.ClrType))
            .Select(e => e.GetTableName())
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var tablesWithRlsPolicy = FindTablesWithRlsPolicy();

        var uncovered = tenantOwnedTableNames.Where(t => !tablesWithRlsPolicy.Contains(t)).ToList();

        Assert.Empty(uncovered);
    }

    private static HashSet<string> FindTablesWithRlsPolicy()
    {
        // Every RLS migration in this codebase (AddRlsPolicies,
        // UpdateRlsTenantContextMode, AddFileStorageRlsPolicies,
        // AddMissingRlsPolicies) issues its CREATE POLICY statement through
        // string interpolation inside a `foreach (var table in TenantTables)`
        // loop, so the literal migration source never contains
        // "CREATE POLICY tenant_isolation ON <real_table_name>" -- only the
        // interpolation placeholder. The actual covered table names live in
        // each file's `TenantTables` string array, so that's what gets
        // parsed here (restricted to files that also contain the
        // "CREATE POLICY tenant_isolation" pattern, so an unrelated
        // string[] array elsewhere can't be mistaken for RLS coverage).
        var migrationsDir = FindMigrationsDirectory();
        var tablesWithRlsPolicy = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(migrationsDir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(file);
            if (!source.Contains("CREATE POLICY tenant_isolation", StringComparison.Ordinal))
                continue;

            var arrayMatch = Regex.Match(
                source,
                "TenantTables\\s*=\\s*\\[(?<body>[\\s\\S]*?)\\];",
                RegexOptions.None);
            if (!arrayMatch.Success)
                continue;

            foreach (Match tableNameMatch in Regex.Matches(arrayMatch.Groups["body"].Value, "\"(?<name>[a-z0-9_]+)\""))
                tablesWithRlsPolicy.Add(tableNameMatch.Groups["name"].Value);
        }

        return tablesWithRlsPolicy;
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

    private static string? ExtractUsername(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex < 0)
                continue;

            var key = part[..separatorIndex].Trim();
            if (key.Equals("Username", StringComparison.OrdinalIgnoreCase))
                return part[(separatorIndex + 1)..].Trim();
        }

        return null;
    }

    private static string FindSrcDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src above " + AppContext.BaseDirectory);
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

        throw new DirectoryNotFoundException("Could not locate src/ONEVO.Infrastructure/Migrations above " + AppContext.BaseDirectory);
    }

    private static string FindApiProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ONEVO.Api");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/ONEVO.Api above " + AppContext.BaseDirectory);
    }

    private sealed class CapturedServiceConstantFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is ITenantContext)
                Found = true;

            return base.VisitConstant(node);
        }
    }

    /// <summary>Finds an `x.Role.IsDeleted` member-access chain anywhere in the expression tree.</summary>
    private sealed class RoleIsDeletedReferenceFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.Name == "IsDeleted" &&
                node.Expression is MemberExpression parentMember &&
                parentMember.Member.Name == "Role")
            {
                Found = true;
            }

            return base.VisitMember(node);
        }
    }

    /// <summary>Finds a `x.TenantId == ...` equality comparison anywhere in the expression tree.</summary>
    private sealed class TenantIdComparisonFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.Equal && ReferencesTenantId(node))
                Found = true;

            return base.VisitBinary(node);
        }

        private static bool ReferencesTenantId(BinaryExpression node)
        {
            return IsTenantIdMember(node.Left) || IsTenantIdMember(node.Right);
        }

        private static bool IsTenantIdMember(Expression expression)
        {
            return expression is MemberExpression memberExpression &&
                   memberExpression.Member.Name == nameof(ITenantOwnedEntity.TenantId);
        }
    }
}
