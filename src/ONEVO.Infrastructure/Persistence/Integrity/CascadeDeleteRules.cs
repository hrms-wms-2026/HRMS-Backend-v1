using Microsoft.EntityFrameworkCore;

namespace ONEVO.Infrastructure.Persistence.Integrity;

/// <summary>
/// Task 39g: a foreign key cascades only when it is on <see cref="Kept"/>. Every other key that a configuration
/// (or the EF convention for a required key) made <c>ON DELETE CASCADE</c> becomes NO ACTION here, so a stray hard
/// delete of a parent fails instead of removing history. A cascade is kept only for a true part of its parent
/// (line items, layout rows, a message's reactions) when the code deletes that parent or its children through it.
/// The reasons are in docs/reports/database/2026-11-25-task-39efg-module-keys-checks-cascades.md, and
/// ForeignKeyRulesTests holds its own copy of the list, so a change to one fails until the other follows.
/// </summary>
public static class CascadeDeleteRules
{
    public static IReadOnlySet<string> Kept { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // UpdateClockInPolicy clears policy.LateDeductionRules.
        "fk_clock_in_late_deduction_rules_clock_in_policies_clock_in_policy_id",
        // DeleteMessageIfExistsAsync hard-deletes a system card with ExecuteDelete; its parts go with it.
        "fk_conversation_message_attachments_conversation_messages_message_id",
        "fk_conversation_message_hides_conversation_messages_message_id",
        "fk_conversation_message_mentions_conversation_messages_message_id",
        "fk_conversation_message_reactions_conversation_messages_message_id",
        "fk_conversation_message_revisions_conversation_messages_message_id",
        // DashboardLayoutSupport removes a widget from dashboard.Widgets and clears widget.Layouts.
        "fk_dashboard_widget_layouts_dashboard_widgets_widget_id",
        "fk_dashboard_widgets_user_dashboards_dashboard_id",
        // A future leave policy version is hard-deleted with its blackout periods.
        "fk_leave_policy_blackout_periods_leave_policy_versions_leave_policy_version_id",
        // PermissionCatalogSynchronizer removes a retired permission; its grants and overrides go by cascade.
        "fk_role_permissions_permissions_permission_id",
        "fk_user_permission_overrides_permissions_permission_id",
        // ArchiveRole removes the role through EF (the soft-delete interceptor keeps the row); EF cascades its tracked parts.
        "fk_role_permissions_roles_role_id",
        "fk_user_roles_roles_role_id",
        // Tasks are removed through EF (soft-deleted); EF cascades their tracked assignments and planner blocks.
        "fk_task_assignments_work_tasks_task_id",
        "fk_planned_task_blocks_work_tasks_task_id",
        // Not reviewed here: CredentialOwnershipCompletionArchitectureTests allows later migrations on payment_gateway tables
        // only the 39c audit key. The configs are never removed, so the cascade never fires.
        "fk_payment_gateway_country_routes_payment_gateway_configs_gateway_config_id",
        "fk_payment_gateway_credentials_payment_gateway_configs_payment_gateway_config_id",
    };

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()))
        {
            if (foreignKey.DeleteBehavior == DeleteBehavior.Cascade && !Kept.Contains(foreignKey.GetConstraintName()!))
            {
                foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
            }
        }
    }
}
