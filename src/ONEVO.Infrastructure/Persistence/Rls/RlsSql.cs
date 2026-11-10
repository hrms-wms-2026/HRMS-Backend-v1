namespace ONEVO.Infrastructure.Persistence.Rls;

public enum RlsPredicate
{
    /// <summary>Admin, or tenant mode with a matching tenant_id.</summary>
    Tenant,

    /// <summary>The Tenant rule, plus system mode. Anonymous tray activation endpoints need it.</summary>
    TenantOrSystem,

    /// <summary>A matching tenant_id only. No admin exception.</summary>
    TenantStrict,

    /// <summary>Reads: the Tenant rule, or the key being looked up. Writes: the Tenant rule.</summary>
    Sessions,

    /// <summary>Reads: the Tenant rule, or the login lookup user. Writes: the Tenant rule.</summary>
    Users,

    /// <summary>
    /// For tables with a nullable tenant_id written by anonymous paths: the TenantOrSystem rule, except
    /// that a tenant may also insert an unowned row. It can never read one back.
    /// </summary>
    PlatformShared,
}

/// <summary>
/// The T-SQL behind tenant isolation. The predicates fail closed: a missing or unreadable session
/// context matches no rows. Every new tenant-owned table needs a CreatePolicy call in its migration
/// and an entry in RlsTableRegistry.
/// </summary>
public static class RlsSql
{
    public const string LookupUser = "onevo_auth_base_login_fn_owner";

    private const string Mode = "CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16))";
    private const string TenantMatch =
        "@tenant_id = TRY_CONVERT(uniqueidentifier, CAST(SESSION_CONTEXT(N'app.current_tenant_id') AS nvarchar(36)))";
    private const string TenantRule = $"{Mode} = N'admin' OR ({Mode} = N'tenant' AND {TenantMatch})";

    /// <summary>One statement per item: CREATE SCHEMA and CREATE FUNCTION must each start a batch.</summary>
    public static IReadOnlyList<string> CreateFunctions() =>
    [
        "CREATE SCHEMA rls;",
        Function("fn_tenant", "@tenant_id uniqueidentifier", TenantRule),
        Function(
            "fn_tenant_or_system",
            "@tenant_id uniqueidentifier",
            $"{Mode} IN (N'admin', N'system') OR ({Mode} = N'tenant' AND {TenantMatch})"),
        Function("fn_tenant_strict", "@tenant_id uniqueidentifier", TenantMatch),
        Function(
            "fn_platform_shared_insert",
            "@tenant_id uniqueidentifier",
            $"@tenant_id IS NULL OR {Mode} IN (N'admin', N'system') OR ({Mode} = N'tenant' AND {TenantMatch})"),
        Function(
            "fn_sessions_read",
            "@tenant_id uniqueidentifier, @key_hash nvarchar(128)",
            $"{TenantRule} OR @key_hash = CAST(SESSION_CONTEXT(N'app.session_lookup_key_hash') AS nvarchar(128))"),
        Function(
            "fn_users_read",
            "@tenant_id uniqueidentifier",
            $"{TenantRule} OR USER_NAME() = N'{LookupUser}'"),
    ];

    public static string CreatePolicy(string table, RlsPredicate predicate)
    {
        var filter = predicate switch
        {
            RlsPredicate.Tenant => "rls.fn_tenant(tenant_id)",
            RlsPredicate.TenantOrSystem or RlsPredicate.PlatformShared => "rls.fn_tenant_or_system(tenant_id)",
            RlsPredicate.TenantStrict => "rls.fn_tenant_strict(tenant_id)",
            RlsPredicate.Sessions => "rls.fn_sessions_read(tenant_id, key_hash)",
            RlsPredicate.Users => "rls.fn_users_read(tenant_id)",
            _ => throw new ArgumentOutOfRangeException(nameof(predicate)),
        };
        var block = predicate is RlsPredicate.Sessions or RlsPredicate.Users ? "rls.fn_tenant(tenant_id)" : filter;
        var insert = predicate == RlsPredicate.PlatformShared ? "rls.fn_platform_shared_insert(tenant_id)" : block;

        return $"""
            CREATE SECURITY POLICY rls.policy_{table}
                ADD FILTER PREDICATE {filter} ON dbo.{table},
                ADD BLOCK PREDICATE {insert} ON dbo.{table} AFTER INSERT,
                ADD BLOCK PREDICATE {block} ON dbo.{table} AFTER UPDATE,
                ADD BLOCK PREDICATE {block} ON dbo.{table} BEFORE UPDATE,
                ADD BLOCK PREDICATE {block} ON dbo.{table} BEFORE DELETE
                WITH (STATE = ON, SCHEMABINDING = ON);
            """;
    }

    /// <summary>For a migration that must change tenant_id: drop, alter, then CreatePolicy again.</summary>
    public static string DropPolicy(string table) => $"DROP SECURITY POLICY rls.policy_{table};";

    private static string Function(string name, string parameters, string rule) => $"""
        CREATE FUNCTION rls.{name}({parameters})
        RETURNS TABLE WITH SCHEMABINDING AS RETURN
            SELECT 1 AS allowed WHERE {rule};
        """;
}
