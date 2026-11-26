using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ONEVO.Infrastructure.Persistence.Integrity;

namespace ONEVO.Tests.Architecture.Schema;

/// <summary>Task 39 rules for foreign keys, checked on the EF model.</summary>
public sealed class ForeignKeyRulesTests
{
    /// <summary>Every foreign key needs an index (or key) whose leading columns are the key's columns,
    /// so the check on a parent delete and joins on the key do not scan the child table.</summary>
    [Fact]
    public void EveryForeignKeyHasAnIndexThatStartsWithIt()
    {
        var offenders = new List<string>();
        foreach (var entity in SchemaIntegrityInventory.Model().GetEntityTypes().Where(e => e.GetTableName() is not null))
        {
            var leading = entity.GetIndexes().Select(index => index.Properties)
                .Concat(entity.GetKeys().Select(key => key.Properties))
                .ToList();
            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var covered = leading.Any(columns => columns.Count >= foreignKey.Properties.Count
                    && columns.Take(foreignKey.Properties.Count).SequenceEqual(foreignKey.Properties));
                if (!covered)
                    offenders.Add($"{foreignKey.GetConstraintName()} ({string.Join(", ", foreignKey.Properties.Select(p => p.Name))})");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>A key to a person (employees, users, platform_users) never cascades: removing a person must not
    /// delete their history.</summary>
    [Fact]
    public void NoForeignKeyToAPersonCascades()
    {
        string[] people = ["employees", "users", "platform_users"];
        var offenders = SchemaIntegrityInventory.Model().GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(foreignKey => people.Contains(foreignKey.PrincipalEntityType.GetTableName())
                && foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade)
            .Select(foreignKey => foreignKey.GetConstraintName())
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Task 39g: the cascades that are kept, each with its reason. Every other key is NO ACTION
    /// (CascadeDeleteRules), so a stray hard delete of a parent fails instead of removing history. A new cascade
    /// needs a line here and in CascadeDeleteRules.Kept.</summary>
    private static readonly Dictionary<string, string> KeptCascades = new(StringComparer.Ordinal)
    {
        ["fk_clock_in_late_deduction_rules_clock_in_policies_clock_in_policy_id"] = "line items: UpdateClockInPolicy clears policy.LateDeductionRules",
        ["fk_conversation_message_attachments_conversation_messages_message_id"] = "part of a message: DeleteMessageIfExistsAsync hard-deletes a system card",
        ["fk_conversation_message_hides_conversation_messages_message_id"] = "part of a message: DeleteMessageIfExistsAsync hard-deletes a system card",
        ["fk_conversation_message_mentions_conversation_messages_message_id"] = "part of a message: DeleteMessageIfExistsAsync hard-deletes a system card",
        ["fk_conversation_message_reactions_conversation_messages_message_id"] = "part of a message: DeleteMessageIfExistsAsync hard-deletes a system card",
        ["fk_conversation_message_revisions_conversation_messages_message_id"] = "part of a message: DeleteMessageIfExistsAsync hard-deletes a system card",
        ["fk_dashboard_widget_layouts_dashboard_widgets_widget_id"] = "layout rows: DashboardLayoutSupport clears widget.Layouts",
        ["fk_dashboard_widgets_user_dashboards_dashboard_id"] = "layout rows: DashboardLayoutSupport removes a widget from dashboard.Widgets",
        ["fk_leave_policy_blackout_periods_leave_policy_versions_leave_policy_version_id"] = "part of a version: a future policy version is hard-deleted",
        ["fk_role_permissions_permissions_permission_id"] = "PermissionCatalogSynchronizer removes a retired permission and relies on the cascade",
        ["fk_user_permission_overrides_permissions_permission_id"] = "PermissionCatalogSynchronizer removes a retired permission and relies on the cascade",
        ["fk_role_permissions_roles_role_id"] = "ArchiveRole removes the role through EF; EF cascades its tracked parts",
        ["fk_user_roles_roles_role_id"] = "ArchiveRole removes the role through EF; EF cascades its tracked parts",
        ["fk_task_assignments_work_tasks_task_id"] = "tasks are removed through EF; EF cascades their tracked assignments",
        ["fk_planned_task_blocks_work_tasks_task_id"] = "tasks are removed through EF; EF cascades their tracked planner blocks",
        ["fk_inbox_item_assignees_inbox_entries_inbox_entry_id"] = "assignee rows of an entry: InboxEntryConsistencyJob hard-deletes orphan entries",
        ["fk_payment_gateway_country_routes_payment_gateway_configs_gateway_config_id"] = "left as it is: CredentialOwnershipCompletionArchitectureTests limits later payment_gateway migrations to the 39c audit key",
        ["fk_payment_gateway_credentials_payment_gateway_configs_payment_gateway_config_id"] = "left as it is: CredentialOwnershipCompletionArchitectureTests limits later payment_gateway migrations to the 39c audit key",
    };

    [Fact]
    public void EveryCascadeIsOnTheAllowList()
    {
        var cascades = SchemaIntegrityInventory.Model().GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(foreignKey => foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade)
            .Select(foreignKey => foreignKey.GetConstraintName()!)
            .ToHashSet(StringComparer.Ordinal);

        var notListed = cascades.Where(name => !KeptCascades.ContainsKey(name)).Order(StringComparer.Ordinal).ToList();
        var stale = KeptCascades.Keys.Where(name => !cascades.Contains(name)).Order(StringComparer.Ordinal).ToList();

        Assert.True(notListed.Count == 0, "Cascades without a reason:" + Environment.NewLine + string.Join(Environment.NewLine, notListed));
        Assert.True(stale.Count == 0, "Listed cascades that no longer cascade:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
        Assert.Equal(KeptCascades.Keys.Order(StringComparer.Ordinal), CascadeDeleteRules.Kept.Order(StringComparer.Ordinal));
    }
}
