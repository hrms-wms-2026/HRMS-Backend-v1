using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ONEVO.Infrastructure.Migrations;

namespace ONEVO.Tests.Architecture;

public sealed class LoginWorkspaceSelectionChallengeStorageArchitectureTests
{
    [Fact]
    public void AddLoginWorkspaceSelectionChallengesMigration_CreatesOnlyThatTable()
    {
        var migration = (Migration)new AddLoginWorkspaceSelectionChallenges();

        var createTableOperations = migration.UpOperations.OfType<CreateTableOperation>().ToList();
        createTableOperations.Should().HaveCount(1);
        createTableOperations[0].Name.Should().Be("login_workspace_selection_challenges");
    }

    [Fact]
    public void AddLoginWorkspaceSelectionChallengesMigration_HasExactlyTheApprovedColumns()
    {
        var migration = (Migration)new AddLoginWorkspaceSelectionChallenges();

        var table = migration.UpOperations.OfType<CreateTableOperation>().Single();
        var columnNames = table.Columns.Select(c => c.Name).ToList();

        columnNames.Should().BeEquivalentTo(new[]
        {
            "id",
            "challenge_hash",
            "normalized_email",
            "candidate_workspaces_json",
            "purpose",
            "expires_at",
            "consumed_at",
            "failed_attempt_count",
            "created_at",
            "ip_address",
            "user_agent"
        });
    }

    [Fact]
    public void AddLoginWorkspaceSelectionChallengesMigration_HasNoTenantIdColumn()
    {
        var migration = (Migration)new AddLoginWorkspaceSelectionChallenges();

        var table = migration.UpOperations.OfType<CreateTableOperation>().Single();
        table.Columns.Should().NotContain(c => c.Name == "tenant_id",
            "this table is pre-tenant and must never carry a tenant_id column");
    }

    [Fact]
    public void AddLoginWorkspaceSelectionChallengesMigration_DeclaresRequiredIndexes()
    {
        var migration = (Migration)new AddLoginWorkspaceSelectionChallenges();

        var indexes = migration.UpOperations.OfType<CreateIndexOperation>().ToList();

        indexes.Should().Contain(i => i.IsUnique && i.Columns.SequenceEqual(new[] { "challenge_hash" }));
        indexes.Should().Contain(i => i.Columns.SequenceEqual(new[] { "expires_at" }));
        indexes.Should().Contain(i => i.Columns.SequenceEqual(new[] { "normalized_email", "created_at" }));
    }

    [Fact]
    public void AddLoginWorkspaceSelectionChallengesMigration_DeclaresRequiredCheckConstraints()
    {
        var migration = (Migration)new AddLoginWorkspaceSelectionChallenges();

        var table = migration.UpOperations.OfType<CreateTableOperation>().Single();

        table.CheckConstraints.Should().Contain(c => c.Name == "ck_login_workspace_selection_challenges_purpose");
        table.CheckConstraints.Should().Contain(c => c.Name == "ck_login_workspace_selection_challenges_failed_attempt_count");
    }

    [Fact]
    public void AddLoginWorkspaceSelectionChallengesMigration_TouchesNoOtherTable()
    {
        var migration = (Migration)new AddLoginWorkspaceSelectionChallenges();

        foreach (var operation in migration.UpOperations)
        {
            var tableName = operation switch
            {
                CreateTableOperation createTable => createTable.Name,
                CreateIndexOperation createIndex => createIndex.Table,
                AddCheckConstraintOperation addCheck => addCheck.Table,
                _ => null
            };

            tableName.Should().Be(
                "login_workspace_selection_challenges",
                $"this migration must only create login_workspace_selection_challenges, but found {operation.GetType().Name}");
        }
    }
}
