using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

/// <summary>
/// The "resolved" lines already in people chats said only "Request approved" / "Case resolved". New lines name what was
/// decided and who decided it (InboxChatProjector, from the item's latest inbox journal decision); this rewrites the
/// existing ones the same way - "Annual Leave approved by Maya Fernando", "Late clock-in kept on record by …" - with the
/// decision's note (first 300 characters) in the event JSON. Who decided comes from the journal; a leave request decided
/// outside the Inbox (seeded history, the old approvals page) falls back to its own approver records. Lines with neither
/// are left as they are (the chat still works out what was decided from the title). The new fields are appended to the
/// original JSON text, so the line keeps its {"type":"case_resolved" prefix.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021150000_BackfillDecisionLineDeciders")]
public partial class BackfillDecisionLineDeciders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // conversation_messages, inbox_decision_journal, leave and employee rows are under FORCE RLS; without admin mode
        // this updates nothing.
        migrationBuilder.Sql("SELECT set_config('app.tenant_context_mode', 'admin', true);");
        migrationBuilder.Sql(@"
            WITH lines AS (
                SELECT m.id, m.tenant_id, m.system_event_json AS txt, m.system_event_json::jsonb AS ev
                FROM conversation_messages m
                WHERE m.kind = 'system'
                  AND m.system_event_json LIKE '{""type"":""case_resolved""%}'
                  AND (m.system_event_json::jsonb -> 'byName') IS NULL
            ),
            latest AS (
                SELECT DISTINCT ON (j.tenant_id, j.source, j.kind, j.item_id)
                       j.tenant_id, j.source, j.kind, j.item_id, j.action, j.actor_employee_id, j.comment
                FROM inbox_decision_journal j
                WHERE NOT j.is_deleted
                ORDER BY j.tenant_id, j.source, j.kind, j.item_id, j.decided_at DESC
            ),
            journal_decision AS (
                SELECT l.id, d.actor_employee_id AS by_id, d.action, d.comment
                FROM lines l
                JOIN latest d ON d.tenant_id = l.tenant_id
                             AND d.source = l.ev ->> 'source'
                             AND d.kind = l.ev ->> 'kind'
                             AND d.item_id = (l.ev ->> 'id')::uuid
                WHERE d.action NOT IN ('escalate', 'revert')
                  AND d.actor_employee_id <> '00000000-0000-0000-0000-000000000000'
            ),
            leave_decision AS (
                SELECT l.id, coalesce(rejected.approver_employee_id, lr.approved_by) AS by_id,
                       coalesce(rejected.comment, approved.comment) AS comment
                FROM lines l
                JOIN leave_requests lr ON lr.tenant_id = l.tenant_id AND lr.id = (l.ev ->> 'id')::uuid
                LEFT JOIN LATERAL (
                    SELECT a.approver_employee_id, a.comment
                    FROM leave_request_approvers a
                    WHERE a.tenant_id = lr.tenant_id AND a.leave_request_id = lr.id AND a.status = 'rejected'
                    ORDER BY a.decided_at DESC NULLS LAST
                    LIMIT 1
                ) rejected ON lr.status = 'rejected'
                LEFT JOIN LATERAL (
                    SELECT a.comment
                    FROM leave_request_approvers a
                    WHERE a.tenant_id = lr.tenant_id AND a.leave_request_id = lr.id AND a.status = 'approved'
                      AND a.approver_employee_id = lr.approved_by
                    LIMIT 1
                ) approved ON lr.status = 'approved'
                WHERE l.ev ->> 'source' = 'leave' AND l.ev ->> 'kind' = 'request'
            ),
            decided AS (
                SELECT l.id, l.tenant_id, l.txt, l.ev,
                       regexp_replace(coalesce(l.ev ->> 'title', ''), '^(.*) - .*$', '\1') AS what,
                       coalesce(j.by_id, v.by_id) AS by_id,
                       CASE WHEN j.action LIKE 'resolve:%' THEN substr(j.action, 9) END AS outcome,
                       CASE WHEN j.id IS NOT NULL THEN j.comment ELSE v.comment END AS full_note
                FROM lines l
                LEFT JOIN journal_decision j ON j.id = l.id
                LEFT JOIN leave_decision v ON v.id = l.id
            ),
            named AS (
                SELECT d.*,
                       nullif(trim(coalesce(e.first_name, '') || ' ' || coalesce(e.last_name, '')), '') AS by_name,
                       CASE WHEN length(d.full_note) > 300 THEN rtrim(left(d.full_note, 300)) || '…' ELSE d.full_note END AS note
                FROM decided d
                JOIN employees e ON e.tenant_id = d.tenant_id AND e.id = d.by_id
            ),
            rewritten AS (
                SELECT id,
                       what || ' ' ||
                       CASE WHEN ev ->> 'category' = 'request'
                            THEN lower(replace(coalesce(ev ->> 'status', 'closed'), '_', ' '))
                            ELSE CASE outcome
                                     WHEN 'excused' THEN 'cleared - no issue'
                                     WHEN 'confirmed' THEN 'kept on record'
                                     WHEN 'convert_to_leave' THEN 'counted as leave'
                                     ELSE coalesce('resolved - ' || lower(replace(outcome, '_', ' ')), 'resolved')
                                 END
                       END || ' by ' || by_name AS body,
                       left(txt, length(txt) - 1) || ',' ||
                       substr(json_build_object('what', what, 'by', by_id, 'byName', by_name, 'outcome', outcome, 'comment', note)::text, 2) AS json
                FROM named
                WHERE by_name IS NOT NULL
            )
            UPDATE conversation_messages m
            SET body_html = replace(replace(replace(r.body, '&', '&amp;'), '<', '&lt;'), '>', '&gt;'),
                system_event_json = r.json
            FROM rewritten r
            WHERE m.id = r.id
              AND length(r.json) <= 2000;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data only: the richer wording stays (the frontend reads both shapes).
    }
}
