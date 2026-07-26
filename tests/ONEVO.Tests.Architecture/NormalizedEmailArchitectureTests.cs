using System.Reflection;
using FluentAssertions;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Architecture;

public sealed class NormalizedEmailArchitectureTests
{
    [Fact]
    public void User_HasNormalizedEmailProperty_WithNoPublicSetter()
    {
        var property = typeof(User).GetProperty(
            "NormalizedEmail", BindingFlags.Public | BindingFlags.Instance);

        property.Should().NotBeNull("users must carry a DB-generated normalized_email property");
        property!.SetMethod.Should().NotBeNull();
        property.SetMethod!.IsPublic.Should().BeFalse(
            "NormalizedEmail must be database-generated, never application-written");
    }

    [Fact]
    public void UserConfiguration_MapsNormalizedEmailAsStoredGeneratedColumn()
    {
        var source = ReadSource(
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations",
            "InfrastructureModule", "User", "UserConfiguration.cs");

        source.Should().Contain("normalized_email");
        source.Should().Contain("lower(trim(email))");
        source.Should().Contain("stored: true");
    }

    [Fact]
    public void UserConfiguration_DeclaresTenantNormalizedEmailUniqueIndex()
    {
        var source = ReadSource(
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations",
            "InfrastructureModule", "User", "UserConfiguration.cs");

        source.Should().Contain("ix_users_tenant_id_normalized_email");
    }

    [Fact]
    public void AuthLookupFunction_ComparesNormalizedEmail_NotEmail()
    {
        var migrationPath = FindLatestNormalizedEmailMigration();
        var text = File.ReadAllText(migrationPath);
        var upBody = text.Substring(0, text.IndexOf("protected override void Down", StringComparison.Ordinal));

        upBody.Should().Contain("u.normalized_email = p_normalized_email");
        upBody.Should().NotContain("u.email = p_normalized_email",
            "the forward (Up) function definition must compare normalized_email, not email - " +
            "Down() legitimately reverts to the email comparison and is intentionally excluded here");
    }

    [Fact]
    public void AuthLookupFunctionOwnerGrant_IncludesNormalizedEmail_NotBroadSelect()
    {
        var migrationPath = FindLatestNormalizedEmailMigration();
        var text = File.ReadAllText(migrationPath);

        text.Should().Contain(
            "GRANT SELECT (tenant_id, id, normalized_email, is_active, is_deleted, password_hash) ON public.users");
        text.Should().NotContain("GRANT SELECT ON public.users");
    }

    [Fact]
    public void MigrationContainsDuplicateEmailPrecheck()
    {
        var migrationPath = FindLatestNormalizedEmailMigration();
        var text = File.ReadAllText(migrationPath);

        text.Should().Contain("HAVING count(*) > 1");
        text.Should().Contain("RAISE EXCEPTION");
    }

    private static string FindLatestNormalizedEmailMigration()
    {
        var migrationsDir = Path.Combine(FindRepositoryRoot(), "src", "ONEVO.Infrastructure", "Migrations");
        var match = Directory.EnumerateFiles(migrationsDir, "*_AddUsersNormalizedEmail.cs").SingleOrDefault();
        match.Should().NotBeNull("expected a single AddUsersNormalizedEmail migration file");
        return match!;
    }

    private static string ReadSource(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "ONEVO.Api")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
