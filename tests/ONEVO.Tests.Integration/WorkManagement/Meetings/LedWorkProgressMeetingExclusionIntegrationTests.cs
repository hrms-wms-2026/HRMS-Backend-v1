using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Migrations;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>
/// End-to-end proof that a meeting occurrence never reaches the Team Progress numbers.
/// <para>
/// The Team Progress read path differs between the meetings branch and the branch that
/// introduced the module-hour summaries: one reads the trigger-maintained summary table, the
/// other aggregates tasks itself. Both must exclude meeting occurrences, and the only part that
/// can let one through is the database trigger - so this test drives the real
/// <c>GetLedWorkProgressQueryHandler</c> and reads the numbers out of its response by reflection,
/// which keeps the file compiling against either response shape.
/// </para>
/// <para>
/// The handler is also constructed reflectively for the same reason: the summary repository is
/// one more constructor argument on the branch that has it, and absent on the branch that does
/// not.
/// </para>
/// </summary>
public sealed class LedWorkProgressMeetingExclusionIntegrationTests
{
    private sealed class StubUser(Guid tenantId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => string.Empty;
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private static WorkApprovalEligibility Eligibility(ApplicationDbContext db) => new(
        new EfWorkApprovalRequestRepository(db),
        new WorkHierarchyService(
            new EfObjectiveRepository(db),
            new MilestoneMembershipCoordinator(
                new CommonEfEmployeeRepository(db), new EfProjectMemberRepository(db), new EfObjectiveRepository(db),
                new EfProjectMemberInvitationRepository(db),
                new CallerIdentityResolver(new CommonEfEmployeeRepository(db), new ONEVO.Infrastructure.Persistence.Repositories.EfEntityAssetRepository(db)),
                new OutboxWriter(db, new NoOpEncryptionService(), new SystemDateTimeProvider()))),
        new EfObjectiveRepository(db));

    private static WorkLeadershipService WorkLeadership(ApplicationDbContext db) => new(
        new EfObjectiveRepository(db),
        new EfProjectMemberRepository(db),
        new EfWorkApprovalRequestRepository(db),
        Eligibility(db),
        NullLogger<WorkLeadershipService>.Instance);

    /// <summary>
    /// Builds the handler with whatever repositories its constructor asks for. The summary
    /// repository only exists on the branch that has the summary table; it is supplied by type
    /// name when the constructor wants it.
    /// </summary>
    private static GetLedWorkProgressQueryHandler Handler(ApplicationDbContext db, Guid tenantId, Guid userId)
    {
        var ctor = typeof(GetLedWorkProgressQueryHandler).GetConstructors().Single();
        var args = ctor.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(ICurrentUser)) return (object)new StubUser(tenantId, userId);
            if (p.ParameterType == typeof(IDateTimeProvider)) return new SystemDateTimeProvider();
            if (p.ParameterType == typeof(ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository))
                return new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db);
            if (p.ParameterType == typeof(IWorkLeadershipService)) return WorkLeadership(db);
            if (p.ParameterType == typeof(ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces.IWorkTaskRepository))
                return new EfWorkTaskRepository(db);
            if (p.ParameterType == typeof(ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces.IProjectRepository))
                return new EfProjectRepository(db);
            if (p.ParameterType.Name == "IModuleHourSummaryRepository")
                return Activator.CreateInstance(
                    Type.GetType("ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.EfModuleHourSummaryRepository, ONEVO.Infrastructure")!, db);
            throw new InvalidOperationException($"Unhandled handler parameter: {p.ParameterType.FullName}");
        }).ToArray();

        return (GetLedWorkProgressQueryHandler)ctor.Invoke(args);
    }

    /// <summary>The pre-fix summary schema, as AddModuleHourSummaries creates it.</summary>
    private const string PreFixSchema = """
        CREATE TABLE wm_module_hour_summaries (
            id uuid NOT NULL,
            project_id uuid NOT NULL,
            not_started_hours numeric(18,2) NOT NULL,
            in_progress_hours numeric(18,2) NOT NULL,
            completed_hours numeric(18,2) NOT NULL,
            task_count integer NOT NULL,
            unestimated_task_count integer NOT NULL,
            earliest_open_due_date date NULL,
            tenant_id uuid NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NULL,
            created_by_id uuid NOT NULL,
            is_deleted boolean NOT NULL,
            deleted_at timestamptz NULL,
            CONSTRAINT pk_wm_module_hour_summaries PRIMARY KEY (id)
        );

        CREATE OR REPLACE FUNCTION wm_refresh_module_hour_summary(p_objective_id uuid) RETURNS void
        LANGUAGE sql AS $fn$
            INSERT INTO wm_module_hour_summaries AS s
                (id, tenant_id, project_id, not_started_hours, in_progress_hours, completed_hours,
                 task_count, unestimated_task_count, earliest_open_due_date,
                 created_at, updated_at, created_by_id, is_deleted)
            SELECT
                o.id, o.tenant_id, o.project_id,
                COALESCE(SUM(t.estimated_hours) FILTER (WHERE NOT x.done AND t.progress_percent <= 0), 0),
                COALESCE(SUM(t.estimated_hours) FILTER (WHERE NOT x.done AND t.progress_percent > 0), 0),
                COALESCE(SUM(t.estimated_hours) FILTER (WHERE x.done), 0),
                COUNT(t.id),
                COUNT(t.id) FILTER (WHERE COALESCE(t.estimated_hours, 0) = 0),
                MIN(t.due_date) FILTER (WHERE NOT x.done),
                now(), now(), '00000000-0000-0000-0000-000000000000', false
            FROM objectives o
            LEFT JOIN tasks t
                ON t.objective_id = o.id AND t.tenant_id = o.tenant_id
                AND t.parent_task_id IS NULL AND NOT t.is_deleted
            LEFT JOIN task_statuses st ON st.id = t.status_id
            CROSS JOIN LATERAL (
                SELECT (COALESCE(st.marks_task_complete, false) OR COALESCE(t.progress_percent, 0) >= 100) AS done
            ) x
            WHERE o.id = p_objective_id
            GROUP BY o.id, o.tenant_id, o.project_id
            ON CONFLICT (id) DO UPDATE SET
                not_started_hours = EXCLUDED.not_started_hours,
                in_progress_hours = EXCLUDED.in_progress_hours,
                completed_hours = EXCLUDED.completed_hours,
                task_count = EXCLUDED.task_count,
                unestimated_task_count = EXCLUDED.unestimated_task_count,
                earliest_open_due_date = EXCLUDED.earliest_open_due_date,
                updated_at = now();
        $fn$;

        CREATE OR REPLACE FUNCTION wm_tasks_refresh_module_hour_summary() RETURNS trigger
        LANGUAGE plpgsql AS $fn$
        BEGIN
            IF TG_OP <> 'DELETE' THEN
                PERFORM wm_refresh_module_hour_summary(NEW.objective_id);
            END IF;
            IF TG_OP = 'DELETE' OR (TG_OP = 'UPDATE' AND OLD.objective_id IS DISTINCT FROM NEW.objective_id) THEN
                PERFORM wm_refresh_module_hour_summary(OLD.objective_id);
            END IF;
            RETURN NULL;
        END;
        $fn$;

        CREATE TRIGGER trg_tasks_module_hour_summary_insert_delete
            AFTER INSERT OR DELETE ON tasks
            FOR EACH ROW EXECUTE FUNCTION wm_tasks_refresh_module_hour_summary();

        CREATE TRIGGER trg_tasks_module_hour_summary_update
            AFTER UPDATE ON tasks
            FOR EACH ROW
            WHEN (OLD.objective_id IS DISTINCT FROM NEW.objective_id
                OR OLD.status_id IS DISTINCT FROM NEW.status_id
                OR OLD.estimated_hours IS DISTINCT FROM NEW.estimated_hours
                OR OLD.progress_percent IS DISTINCT FROM NEW.progress_percent
                OR OLD.due_date IS DISTINCT FROM NEW.due_date
                OR OLD.parent_task_id IS DISTINCT FROM NEW.parent_task_id
                OR OLD.is_deleted IS DISTINCT FROM NEW.is_deleted)
            EXECUTE FUNCTION wm_tasks_refresh_module_hour_summary();
        """;

    private static async Task ApplyMigrationAsync(NpgsqlConnection conn)
    {
        var migration = new ExcludeMeetingTasksFromModuleHourSummaries();
        foreach (var sql in migration.UpOperations.OfType<SqlOperation>())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql.Sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Reads a named integer property off an object, wherever it sits in the shape.</summary>
    private static int? TryReadInt(object obj, string property)
        => obj.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj) is { } value
            ? Convert.ToInt32(value)
            : null;

    [Fact]
    public async Task TheTeamProgressResponse_ExcludesTheMeetingOccurrence()
    {
        var helper = await MyTeamDb.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid leadUserId, moduleId;

        await using (var db = helper.NewContext())
        {
            var creator = helper.AddEmployee(db);
            var lead = helper.AddEmployee(db);
            leadUserId = lead.UserId;
            var (projectId, rootId) = helper.AddProject(db, creator.Id);
            var module = helper.AddModule(db, projectId, rootId, lead.Id);
            moduleId = module;
            helper.AddMember(db, projectId, module, lead.Id);
            var doing = helper.AddStatus(db, projectId, false);
            await db.SaveChangesAsync();   // the Module exists before any task points at it

            // ONE standard task with an estimate.
            var standard = helper.AddTask(db, projectId, module, doing, 0, today.AddDays(5),
                assigneeEmployeeId: lead.Id);
            standard.EstimatedHours = 4m;

            // ONE meeting occurrence in the SAME module.
            var placement = new MeetingPlacement(lead.Id, projectId, module, doing);
            var series = helper.AddSeries(db, placement);
            helper.AddOccurrence(db, series, placement);
            await db.SaveChangesAsync();
        }

        // The branch that has the summary table needs it - and the fix - to be in place.
        await using (var conn = new NpgsqlConnection(helper.ConnectionString))
        {
            await conn.OpenAsync();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT count(*) FROM pg_class WHERE relname = 'wm_module_hour_summaries'";
                var exists = (long)(await cmd.ExecuteScalarAsync())!;
                if (exists > 0)
                {
                    await using var create = conn.CreateCommand();
                    create.CommandText = PreFixSchema;
                    await create.ExecuteNonQueryAsync();
                    await ApplyMigrationAsync(conn);
                }
            }
        }

        await using (var db = helper.NewContext())
        {
            var result = await Handler(db, helper.TenantId, leadUserId).Handle(new GetLedWorkProgressQuery(), default);
            result.IsSuccess.Should().BeTrue();

            var response = result.Value!;
            var projects = (System.Collections.IEnumerable)response.GetType().GetProperty("Projects")!.GetValue(response)!;
            var project = projects.Cast<object>().Single();
            var modules = (System.Collections.IEnumerable)project.GetType().GetProperty("Modules")!.GetValue(project)!;
            var head = modules.Cast<object>().Single(m => (Guid)m.GetType().GetProperty("ObjectiveId")!.GetValue(m)! == moduleId);

            // The Module's hours: on the branch that reads the summary table these are the
            // trigger-maintained numbers; on the branch that aggregates they are the task counts.
            var hours = head.GetType().GetProperty("Hours")?.GetValue(head)
                        ?? head.GetType().GetProperty("Totals")!.GetValue(head)!;

            // The task count is named TaskCount where the summary table is read, and Total on
            // the branch that counts tasks itself. Either way it is the number of work items
            // the Module shows, and the meeting occurrence must not be one of them.
            var taskCount = TryReadInt(hours, "TaskCount") ?? TryReadInt(hours, "Total");
            taskCount.Should().NotBeNull("the response should expose a task count");
            taskCount.Should().Be(1, "the meeting occurrence must not be counted");

            // The unestimated count only exists where the summary table is read. A meeting
            // occurrence carries no estimate, so counting it would inflate this too.
            if (TryReadInt(hours, "UnestimatedTaskCount") is { } unestimated)
                unestimated.Should().Be(0, "the meeting occurrence carries no estimate");
        }
    }
}
