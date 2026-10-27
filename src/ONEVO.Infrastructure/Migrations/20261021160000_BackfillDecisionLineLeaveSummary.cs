using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

/// <summary>
/// Decision cards now show the item's one-line summary under the heading ("Sick Leave · 15 Sep – 15 Sep · 8 h");
/// InboxChatProjector stores it on new lines. This adds it to the existing leave request lines, built the way the leave
/// inbox subtitle is (type · start – end · hours, dates as stored in UTC). Other kinds keep no summary until their next
/// decision line. Appended to the original JSON text, so the line keeps its {"type":"case_resolved" prefix.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021160000_BackfillDecisionLineLeaveSummary")]
public partial class BackfillDecisionLineLeaveSummary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // conversation_messages and leave_requests are under FORCE RLS; without admin mode this updates nothing.
        migrationBuilder.Sql("SELECT set_config('app.tenant_context_mode', 'admin', true);");
        migrationBuilder.Sql(@"
            WITH lines AS (
                SELECT m.id, m.tenant_id, m.system_event_json AS txt, m.system_event_json::jsonb AS ev
                FROM conversation_messages m
                WHERE m.kind = 'system'
                  AND m.system_event_json LIKE '{""type"":""case_resolved""%}'
                  AND (m.system_event_json::jsonb -> 'summary') IS NULL
                  AND m.system_event_json::jsonb ->> 'source' = 'leave'
                  AND m.system_event_json::jsonb ->> 'kind' = 'request'
            ),
            summarised AS (
                SELECT l.id, l.txt,
                       coalesce(l.ev ->> 'what', regexp_replace(coalesce(l.ev ->> 'title', ''), '^(.*) - .*$', '\1'))
                         || ' · ' || to_char(lr.start_at AT TIME ZONE 'UTC', 'DD Mon')
                         || ' – ' || to_char(lr.end_at AT TIME ZONE 'UTC', 'DD Mon')
                         || ' · ' || trim_scale(lr.total_hours)::text || ' h' AS summary
                FROM lines l
                JOIN leave_requests lr ON lr.tenant_id = l.tenant_id AND lr.id = (l.ev ->> 'id')::uuid
            )
            UPDATE conversation_messages m
            SET system_event_json = left(s.txt, length(s.txt) - 1) || ',' || substr(json_build_object('summary', s.summary)::text, 2)
            FROM summarised s
            WHERE m.id = s.id
              AND length(s.txt) + length(s.summary) < 1960;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data only: the summary stays (the frontend reads lines with and without it).
    }
}
