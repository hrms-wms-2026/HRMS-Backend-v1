using FluentAssertions;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Migrations;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// The hand-written data repair in migration AddAuditColumnForeignKeys39c. created_by_id used Guid.Empty for "no
/// signed-in user", and older seeders and code wrote the wrong kind of identity (a tenant users id where a platform
/// user belongs, and so on); the new keys reject both. The test database already has the keys, so each test switches
/// one off (NOCHECK), writes the old data, runs the repair, and checks the key again. Requires Docker.
/// </summary>
public sealed class AuditCreatorRepairTests
{
    private const string CategoryKey = "fk_project_categories_users_created_by_id";
    private const string SubscriptionKey = "fk_tenant_subscriptions_platform_users_created_by_id";

    [Fact]
    public async Task Repair_turns_guid_empty_into_null_and_the_key_holds_again()
    {
        var w = await SeedAsync();
        await SetCategoryCreatorAsync(w, Guid.Empty);

        await ExecAsync(w.H, AddAuditColumnForeignKeys39c.RepairAuditCreatorsSql);

        (await CreatorAsync(w.H, "project_categories", w.CategoryId)).Should().BeNull();
        await CheckKeyAsync(w.H, "project_categories", CategoryKey);
    }

    [Fact]
    public async Task Repair_clears_a_tenant_users_id_in_a_platform_users_column()
    {
        var w = await SeedAsync();
        var subscriptionId = await AddSubscriptionAsync(w.H);
        // What DevSmokeTestTenantSeeder wrote before task 39c: the tenant owner's users id.
        await ExecAsync(w.H, $"""
            ALTER TABLE dbo.tenant_subscriptions NOCHECK CONSTRAINT {SubscriptionKey};
            UPDATE dbo.tenant_subscriptions SET created_by_id = '{w.LeadUserId}' WHERE id = '{subscriptionId}';
            """);

        await ExecAsync(w.H, AddAuditColumnForeignKeys39c.RepairAuditCreatorsSql);

        (await CreatorAsync(w.H, "tenant_subscriptions", subscriptionId)).Should().BeNull();
        await CheckKeyAsync(w.H, "tenant_subscriptions", SubscriptionKey);
    }

    [Fact]
    public async Task Repair_clears_a_platform_users_id_in_a_users_column()
    {
        var w = await SeedAsync();
        var platformUserId = await AddPlatformUserAsync(w.H);
        await SetCategoryCreatorAsync(w, platformUserId);

        await ExecAsync(w.H, AddAuditColumnForeignKeys39c.RepairAuditCreatorsSql);

        (await CreatorAsync(w.H, "project_categories", w.CategoryId)).Should().BeNull();
        await CheckKeyAsync(w.H, "project_categories", CategoryKey);
    }

    [Fact]
    public async Task Repair_clears_an_employee_id_in_a_users_column()
    {
        var w = await SeedAsync();
        await SetCategoryCreatorAsync(w, w.LeadEmployeeId);

        await ExecAsync(w.H, AddAuditColumnForeignKeys39c.RepairAuditCreatorsSql);

        (await CreatorAsync(w.H, "project_categories", w.CategoryId)).Should().BeNull();
        await CheckKeyAsync(w.H, "project_categories", CategoryKey);
    }

    [Fact]
    public async Task Repair_stops_and_names_the_table_when_a_value_is_in_no_identity_table()
    {
        var w = await SeedAsync();
        var unknown = Guid.NewGuid();
        await SetCategoryCreatorAsync(w, unknown);

        var repair = () => ExecAsync(w.H, AddAuditColumnForeignKeys39c.RepairAuditCreatorsSql);

        var thrown = (await repair.Should().ThrowAsync<SqlException>()).Which;
        thrown.Number.Should().Be(50039);
        thrown.Message.Should().Contain("project_categories=1");
        (await CreatorAsync(w.H, "project_categories", w.CategoryId))
            .Should().Be(unknown, "a value in no identity table is never guessed or removed");
    }

    private sealed record World(MyTeamDb H, Guid CategoryId, Guid LeadEmployeeId, Guid LeadUserId);

    private static async Task<World> SeedAsync()
    {
        var helper = await MyTeamDb.CreateAsync();
        await using var db = helper.NewContext();
        var lead = helper.AddEmployee(db);
        var (projectId, _) = helper.AddProject(db, lead.Id);
        var categoryId = db.Projects.Local.Single(p => p.Id == projectId).CategoryId;
        await db.SaveChangesAsync();
        return new World(helper, categoryId, lead.Id, lead.UserId);
    }

    private static Task SetCategoryCreatorAsync(World w, Guid creator) => ExecAsync(w.H, $"""
        ALTER TABLE dbo.project_categories NOCHECK CONSTRAINT {CategoryKey};
        UPDATE dbo.project_categories SET created_by_id = '{creator}' WHERE id = '{w.CategoryId}';
        """);

    private static async Task<Guid> AddSubscriptionAsync(MyTeamDb helper)
    {
        await using var db = helper.NewContext();
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
        return subscription.Id;
    }

    private static async Task<Guid> AddPlatformUserAsync(MyTeamDb helper)
    {
        await using var db = helper.NewContext();
        var admin = new PlatformUser
        {
            Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@repair-test.onevo.dev", FullName = "Repair Admin",
            Status = PlatformUser.StatusActive,
        };
        db.PlatformUsers.Add(admin);
        await db.SaveChangesAsync();
        return admin.Id;
    }

    private static async Task CheckKeyAsync(MyTeamDb helper, string table, string key)
    {
        var recheck = () => ExecAsync(helper, $"ALTER TABLE dbo.{table} WITH CHECK CHECK CONSTRAINT {key};");
        await recheck.Should().NotThrowAsync();
    }

    private static async Task ExecAsync(MyTeamDb helper, string sql)
    {
        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid?> CreatorAsync(MyTeamDb helper, string table, Guid id)
    {
        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT created_by_id FROM dbo.{table} WHERE id = '{id}';";
        var value = await command.ExecuteScalarAsync();
        return value is Guid creator ? creator : null;
    }
}
