using FluentAssertions;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Migrations;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// The hand-written data repair in migration AddAuditColumnForeignKeys39c: created_by_id used Guid.Empty for "no
/// signed-in user", which the new keys reject. The test database already has the keys, so each test switches one off
/// (NOCHECK) on project_categories, writes the old data, runs the repair, and checks the key again. Requires Docker.
/// </summary>
public sealed class AuditCreatorRepairTests
{
    private const string ForeignKey = "fk_project_categories_users_created_by_id";

    [Fact]
    public async Task Repair_turns_guid_empty_into_null_and_the_key_holds_again()
    {
        var (db, categoryId) = await SeedAsync();
        await ExecAsync(db, $"""
            ALTER TABLE dbo.project_categories NOCHECK CONSTRAINT {ForeignKey};
            UPDATE dbo.project_categories SET created_by_id = '{Guid.Empty}' WHERE id = '{categoryId}';
            """);

        await ExecAsync(db, AddAuditColumnForeignKeys39c.RepairAuditCreatorsSql);

        (await CreatedByAsync(db, categoryId)).Should().BeNull();
        var recheck = () => ExecAsync(db, $"ALTER TABLE dbo.project_categories WITH CHECK CHECK CONSTRAINT {ForeignKey};");
        await recheck.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Repair_stops_and_names_the_table_when_a_value_is_not_a_user()
    {
        var (db, categoryId) = await SeedAsync();
        var unknown = Guid.NewGuid();
        await ExecAsync(db, $"""
            ALTER TABLE dbo.project_categories NOCHECK CONSTRAINT {ForeignKey};
            UPDATE dbo.project_categories SET created_by_id = '{unknown}' WHERE id = '{categoryId}';
            """);

        var repair = () => ExecAsync(db, AddAuditColumnForeignKeys39c.RepairAuditCreatorsSql);

        var thrown = (await repair.Should().ThrowAsync<SqlException>()).Which;
        thrown.Number.Should().Be(50039);
        thrown.Message.Should().Contain("project_categories=1");
        (await CreatedByAsync(db, categoryId)).Should().Be(unknown, "an unknown value is never guessed or removed");
    }

    [Fact]
    public async Task Repair_clears_a_tenant_users_id_the_old_seeder_wrote_into_a_subscription()
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid ownerUserId, subscriptionId;
        await using (var db = helper.NewContext())
        {
            ownerUserId = helper.AddEmployee(db).UserId;
            var plan = new SubscriptionPlan
            {
                Id = Guid.NewGuid(), Name = "Repair Plan", Code = "repair-plan", Tier = "standard",
                IncludedModulesJson = "[]", CompanySizeRange = "11-50", Currency = "USD", IsActive = true,
            };
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var subscription = new TenantSubscription
            {
                Id = Guid.NewGuid(), TenantId = helper.TenantId, PlanId = plan.Id, Status = "active",
                CurrentPeriodStart = today, CurrentPeriodEnd = today.AddDays(30), ContractStartDate = today,
            };
            db.SubscriptionPlans.Add(plan);
            db.TenantSubscriptions.Add(subscription);
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;
        }
        // What DevSmokeTestTenantSeeder wrote before task 39c: the tenant owner's users id, not a platform user.
        await ExecAsync(helper, $"""
            ALTER TABLE dbo.tenant_subscriptions NOCHECK CONSTRAINT {SubscriptionForeignKey};
            UPDATE dbo.tenant_subscriptions SET created_by_id = '{ownerUserId}' WHERE id = '{subscriptionId}';
            """);

        await ExecAsync(helper, AddAuditColumnForeignKeys39c.RepairAuditCreatorsSql);

        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT created_by_id FROM dbo.tenant_subscriptions WHERE id = '{subscriptionId}';";
        (await command.ExecuteScalarAsync()).Should().Be(DBNull.Value);
        var recheck = () => ExecAsync(helper,
            $"ALTER TABLE dbo.tenant_subscriptions WITH CHECK CHECK CONSTRAINT {SubscriptionForeignKey};");
        await recheck.Should().NotThrowAsync();
    }

    private const string SubscriptionForeignKey = "fk_tenant_subscriptions_platform_users_created_by_id";

    private static async Task<(MyTeamDb Db, Guid CategoryId)> SeedAsync()
    {
        var helper = await MyTeamDb.CreateAsync();
        await using var db = helper.NewContext();
        var lead = helper.AddEmployee(db);
        var (projectId, _) = helper.AddProject(db, lead.Id);
        var categoryId = db.Projects.Local.Single(p => p.Id == projectId).CategoryId;
        await db.SaveChangesAsync();
        return (helper, categoryId);
    }

    private static async Task ExecAsync(MyTeamDb helper, string sql)
    {
        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid?> CreatedByAsync(MyTeamDb helper, Guid categoryId)
    {
        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT created_by_id FROM dbo.project_categories WHERE id = '{categoryId}';";
        var value = await command.ExecuteScalarAsync();
        return value is Guid id ? id : null;
    }
}
