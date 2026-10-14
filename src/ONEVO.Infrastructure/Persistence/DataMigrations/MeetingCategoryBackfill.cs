namespace ONEVO.Infrastructure.Persistence.DataMigrations;

/// <summary>
/// Data step of migration AddTaskCategorySystemKey. FROZEN: the migration and its integration
/// test both run this exact text. Never edit it; add a new migration instead.
///
/// ix_task_categories_one_name_per_project is case-sensitive and not filtered by is_deleted, so:
/// (1) promote one live case-variant of "meeting" per project; (2) otherwise restore a
/// soft-deleted exact "meeting" (it still owns the name); (3) otherwise insert one.
/// Idempotent: every step skips projects that already have the system row.
/// </summary>
public static class MeetingCategoryBackfill
{
    public const string Sql = """
        SET LOCAL app.tenant_context_mode = 'admin';

        UPDATE task_categories c
        SET system_key = 'meeting', updated_at = now()
        FROM (
            SELECT DISTINCT ON (tc.tenant_id, tc.project_id) tc.id
            FROM task_categories tc
            WHERE tc.is_deleted = false
              AND lower(tc.name) = 'meeting'
              AND NOT EXISTS (
                  SELECT 1 FROM task_categories s
                  WHERE s.tenant_id = tc.tenant_id AND s.project_id = tc.project_id AND s.system_key = 'meeting')
            ORDER BY tc.tenant_id, tc.project_id, tc.display_order, tc.created_at, tc.id
        ) pick
        WHERE c.id = pick.id;

        UPDATE task_categories c
        SET is_deleted = false, deleted_at = NULL, system_key = 'meeting', updated_at = now()
        FROM (
            SELECT DISTINCT ON (tc.tenant_id, tc.project_id) tc.id
            FROM task_categories tc
            WHERE tc.is_deleted = true
              AND tc.name = 'meeting'
              AND NOT EXISTS (
                  SELECT 1 FROM task_categories s
                  WHERE s.tenant_id = tc.tenant_id AND s.project_id = tc.project_id AND s.system_key = 'meeting')
            ORDER BY tc.tenant_id, tc.project_id, tc.created_at, tc.id
        ) pick
        WHERE c.id = pick.id;

        INSERT INTO task_categories (id, tenant_id, project_id, name, display_order, system_key, created_at, created_by_id, is_deleted)
        SELECT gen_random_uuid(), p.tenant_id, p.id,
               CASE WHEN p.system_purpose = 'office' THEN 'Meeting' ELSE 'meeting' END,
               COALESCE((SELECT max(tc.display_order) FROM task_categories tc
                         WHERE tc.tenant_id = p.tenant_id AND tc.project_id = p.id AND tc.is_deleted = false), -1) + 1,
               'meeting', now(), p.created_by_id, false
        FROM projects p
        WHERE p.is_deleted = false
          AND NOT EXISTS (
              SELECT 1 FROM task_categories s
              WHERE s.tenant_id = p.tenant_id AND s.project_id = p.id AND s.system_key = 'meeting');
        """;
}
