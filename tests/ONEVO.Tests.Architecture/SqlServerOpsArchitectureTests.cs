using System.Text.RegularExpressions;

namespace ONEVO.Tests.Architecture;

/// <summary>Guards the ops/sqlserver setup scripts and the login lookup's narrow rights.</summary>
public sealed class SqlServerOpsArchitectureTests
{
    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([Root(), .. parts]));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "ops")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    [Fact]
    public void BootstrapScript_DoesNotReferenceUsersOrTenantsTables()
    {
        var bootstrap = Read("ops", "sqlserver", "bootstrap-logins.sql");

        Assert.DoesNotContain("dbo.users", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.tenants", bootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupFiles_ContainNoCommittedPasswords()
    {
        var bootstrap = Read("ops", "sqlserver", "bootstrap-logins.sql");
        var setup = Read("ops", "sqlserver", "setup-local-db.ps1");

        Assert.Contains("$(APP_PASSWORD)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("$(MIGRATOR_PASSWORD)", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"PASSWORD\s*=\s*N?'[^$]", RegexOptions.IgnoreCase), bootstrap);
        Assert.Contains("CROWNUS_DB_APP_PASSWORD", setup, StringComparison.Ordinal);
        Assert.Contains("CROWNUS_DB_MIGRATOR_PASSWORD", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupScript_ClearsItsSecretsAfterwards()
    {
        var setup = Read("ops", "sqlserver", "setup-local-db.ps1");

        Assert.Contains("finally", setup, StringComparison.Ordinal);
        Assert.Contains("Remove-Item Env:SQLCMDPASSWORD", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupScript_RunsGrantsOnlyAfterMigrations()
    {
        var setup = Read("ops", "sqlserver", "setup-local-db.ps1");

        var migrations = setup.IndexOf("dotnet ef database update", StringComparison.Ordinal);
        var grants = setup.IndexOf("Invoke-Script \"post-migration-grants.sql\"", StringComparison.Ordinal);

        Assert.InRange(migrations, 0, grants);
    }

    [Fact]
    public void Bootstrap_SetsTheSettingsTenantIsolationNeeds()
    {
        var bootstrap = Read("ops", "sqlserver", "bootstrap-logins.sql");

        Assert.Contains("COLLATE Latin1_General_100_CS_AS_SC", bootstrap, StringComparison.Ordinal);
        Assert.Contains("SET READ_COMMITTED_SNAPSHOT ON", bootstrap, StringComparison.Ordinal);
        Assert.Contains("SET MAXDOP = 1", bootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void AppLogin_IsNeverGivenOwnerOrPolicyRights()
    {
        var scripts = Read("ops", "sqlserver", "bootstrap-logins.sql")
            + Read("ops", "sqlserver", "post-migration-grants.sql");

        Assert.DoesNotMatch(new Regex(@"ALTER ROLE \w+ ADD MEMBER crownus_app", RegexOptions.IgnoreCase), scripts);
        Assert.DoesNotMatch(new Regex(@"GRANT [^;]*SECURITY POLICY[^;]* TO crownus_app", RegexOptions.IgnoreCase), scripts);
        Assert.DoesNotMatch(new Regex(@"GRANT [^;]* ON SCHEMA::rls TO crownus_app", RegexOptions.IgnoreCase), scripts);
    }

    [Fact]
    public void LookupUser_GetsColumnLevelGrantsOnly()
    {
        var grants = Read("ops", "sqlserver", "post-migration-grants.sql");

        Assert.Contains("GRANT SELECT (tenant_id, id, normalized_email, is_active, is_deleted, password_hash)", grants,
            StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT (id, slug, name, status) ON dbo.tenants", grants, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"GRANT SELECT ON [^\n]*crownus_auth_base_login_fn_owner", RegexOptions.IgnoreCase), grants);
    }

    [Fact]
    public void LoginLookup_ComparesTheNormalizedEmail()
    {
        var migration = Read(
            "src", "ONEVO.Infrastructure", "Migrations", "20261108000100_AddAuthLookupAndTriggers.cs");

        Assert.Contains("WHERE u.normalized_email = @normalized_email", migration, StringComparison.Ordinal);
        Assert.Contains("SELECT TOP (9)", migration, StringComparison.Ordinal);
    }
}
