using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.TimeAttendance;

/// <summary>
/// Once per legal-entity-local working day, 2 hours after that legal entity's configured shift
/// start, sends each employee's resolved attendance approver (or, failing that, the legal
/// entity's company-wide coverage owner - see EmployeeAuthorityResolver.ResolveApproverAsync's
/// CompanyCoverage fallback tier) one notification listing that day's late clock-ins.
///
/// Same admin-mode tenant listing + SwitchToTenantAsync mechanism as LeaveYearEndEntitlementJob,
/// but with one DI scope PER TENANT rather than one shared scope for the whole tick - see the
/// comment on RunTickAsync for why. IEmployeeAuthorityResolver depends on ICurrentUser.TenantId,
/// which (after the fix in CurrentUserService) falls back to the ambient ITenantContext this loop
/// sets via SwitchToTenantAsync - unlike LeaveYearEndEntitlementJob, this job CAN use the resolver.
/// </summary>
public sealed class LateClockInDailySummaryJob : BackgroundService
{
    private const string AttendanceReadPermission = "attendance:read";
    private const string TemplateCode = "attendance_late_clockin_daily_summary";
    private const string RelatedEntityType = "attendance_late_daily_summary";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OffsetFromShiftStart = TimeSpan.FromHours(2);

    private readonly IServiceProvider _services;
    private readonly ILogger<LateClockInDailySummaryJob> _logger;
    private readonly Dictionary<Guid, DateOnly> _lastRunShiftDateByLegalEntity = new();

    public LateClockInDailySummaryJob(IServiceProvider services, ILogger<LateClockInDailySummaryJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Late clock-in daily summary job iteration failed; will retry.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Public entry for tests / manual triggers - same precedent as
    /// ActivityDailySummaryJob.RunAggregationAsync / LeaveYearEndEntitlementJob.RunForYearAsync.
    ///
    /// Unlike LeaveYearEndEntitlementJob (which reuses one CreateAsyncScope/DbContext across every
    /// tenant because it never opens an explicit transaction), this job's per-legal-entity work
    /// runs inside IUnitOfWork.ExecuteInTransactionAsync. Reusing one DbContext across tenants
    /// would leave tenant A's tracked Notification entities in the same change tracker while
    /// tenant B's SwitchToTenantAsync flips the ambient RLS tenant underneath it - EF's change
    /// tracker is never cleared by SaveChangesAsync. Each tenant therefore gets its own scope (and
    /// so its own ApplicationDbContext), matching the tenant boundary to the DbContext lifetime
    /// exactly. The one scope created up front is only used to list tenants in admin mode.</summary>
    public async Task RunTickAsync(CancellationToken ct)
    {
        IReadOnlyList<Tenant> tenants;
        await using (var listScope = _services.CreateAsyncScope())
        {
            listScope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetAdminMode();
            var tenantRepository = listScope.ServiceProvider.GetRequiredService<ITenantRepository>();
            tenants = await tenantRepository.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct);
        }

        foreach (var tenant in tenants)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var tenantScope = _services.CreateAsyncScope();
                await ProcessTenantAsync(tenantScope.ServiceProvider, tenant, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // A plain catch here would also swallow ct.ThrowIfCancellationRequested() (or any
                // cancellation-aware call) firing mid-loop during a graceful shutdown and log it as
                // a false "failed for tenant" warning. When cancellation is what's happening, let
                // it propagate so ExecuteAsync's own OperationCanceledException handler deals with
                // it as a normal shutdown instead.
                _logger.LogWarning(ex, "Late clock-in daily summary failed for tenant {TenantId}; skipping.", tenant.Id);
            }
        }
    }

    private async Task ProcessTenantAsync(IServiceProvider services, Tenant tenant, CancellationToken ct)
    {
        var tenantSwitcher = services.GetRequiredService<ITenantContextSwitcher>();
        await tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var legalEntities = services.GetRequiredService<ILegalEntityRepository>();
        var activeLegalEntities = await legalEntities.ListActiveForTenantAsync(tenant.Id, ct);
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var utcNow = clock.UtcNow;

        foreach (var legalEntity in activeLegalEntities)
        {
            ct.ThrowIfCancellationRequested();

            var resolution = AttendanceScheduleResolver.Resolve(legalEntity, utcNow);
            if (resolution.Schedule.Status != "configured"
                || !resolution.Schedule.IsWorkingDay
                || resolution.Schedule.Start is not { } shiftStart)
                continue;

            // resolution.WorkDate and resolution.LocalNow both come from the SAME Resolve() call,
            // so LocalNow.DateTime always falls inside [WorkDate 00:00, WorkDate 23:59:59.999] -
            // it can never reach or exceed WorkDate's midnight boundary. That means naively
            // comparing "today's shift start + offset" (which lands on WorkDate+1 whenever
            // shiftStart is late enough that shiftStart + OffsetFromShiftStart >= 24h, e.g. any
            // shiftStart at or after 22:00 for a 2h offset) against LocalNow is unconditionally
            // true forever: the legal entity would be skipped on every tick, permanently, since by
            // the time "WorkDate+1" actually arrives, WorkDate itself has already been recomputed
            // to be that same later day (today's shift start has moved a day forward too).
            //
            // Instead, resolve the MOST RECENT occurrence of the daily-recurring shift start
            // relative to LocalNow: if today's shift start hasn't happened yet (LocalNow is in the
            // early-morning window before shiftStart), the relevant occurrence is yesterday's. The
            // due instant and the work date whose late records we check are both derived from that
            // occurrence, not from resolution.WorkDate directly - so an overnight shift's due check
            // survives the WorkDate rollover instead of being evaluated against a shift that hasn't
            // started yet.
            //
            // Note: resolution.Schedule.IsWorkingDay is still gated on TODAY's DayOfWeek (from
            // AttendanceScheduleResolver), not shiftWorkDate's. For the early-morning window where
            // shiftWorkDate is yesterday, those two can disagree (e.g. today is a non-working day
            // but yesterday's overnight shift still needs its catch-up check) - that's a pre-existing
            // mismatch in how the resolver defines "today", out of scope for this fix.
            var todayShiftStart = resolution.WorkDate.ToDateTime(shiftStart);
            var mostRecentShiftStart = todayShiftStart <= resolution.LocalNow.DateTime
                ? todayShiftStart
                : todayShiftStart.AddDays(-1);
            var dueAt = mostRecentShiftStart + OffsetFromShiftStart;
            var shiftWorkDate = DateOnly.FromDateTime(mostRecentShiftStart);

            var alreadyRunForShiftDate = _lastRunShiftDateByLegalEntity.TryGetValue(legalEntity.Id, out var lastRun)
                && lastRun == shiftWorkDate;
            if (resolution.LocalNow.DateTime < dueAt || alreadyRunForShiftDate)
                continue;

            try
            {
                await RunForLegalEntityAsync(services, tenant.Id, legalEntity, shiftWorkDate, ct);
                // Only recorded on success: a transient failure (DB blip, one bad row) should be
                // retried on the next 15-minute tick for the rest of today, not silently skipped
                // until tomorrow. The DB-level ExistsAsync check in RunForLegalEntityAsync makes a
                // retry after partial success safe (already-sent recipients are not re-notified).
                _lastRunShiftDateByLegalEntity[legalEntity.Id] = shiftWorkDate;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // One legal entity's failure must not stop the rest of this tenant's legal
                // entities (or the rest of this tenant's tick) from being processed - same
                // per-unit isolation as LeaveYearEndEntitlementJob.RunForYearAsync's per-tenant
                // try/catch. The `when` guard (see the matching one in RunTickAsync) keeps a
                // cancellation firing mid-loop from being misreported as a legal-entity failure.
                _logger.LogWarning(ex,
                    "Late clock-in daily summary failed for legal entity {LegalEntityId} in tenant {TenantId}; will retry next tick.",
                    legalEntity.Id, tenant.Id);
                // Same tenant DbContext is reused for later legal entities. A thrown
                // ExecuteInTransactionAsync rolls back but leaves Added entities tracked;
                // ClearTracking() prevents a later LE's SaveChangesAsync from persisting them.
                services.GetRequiredService<IUnitOfWork>().ClearTracking();
            }
        }
    }

    private async Task RunForLegalEntityAsync(
        IServiceProvider services, Guid tenantId, LegalEntity legalEntity, DateOnly workDate, CancellationToken ct)
    {
        var attendance = services.GetRequiredService<IAttendanceReadRepository>();
        var employees = services.GetRequiredService<
            ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>();
        var authority = services.GetRequiredService<IEmployeeAuthorityResolver>();
        var dispatcher = services.GetRequiredService<INotificationDispatcher>();
        var notifications = services.GetRequiredService<INotificationRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var lateRecords = await attendance.ListByStatusAsync(tenantId, workDate, AttendanceRecord.StatusLate, ct);
        if (lateRecords.Count == 0)
            return;

        var employeesById = await employees.ListByIdsAsync(
            tenantId, lateRecords.Select(r => r.EmployeeId).Distinct().ToArray(), ct);

        var legalEntityLateRecords = lateRecords
            .Where(r => employeesById.TryGetValue(r.EmployeeId, out var employee)
                && employee.LegalEntityId == legalEntity.Id)
            .ToList();
        if (legalEntityLateRecords.Count == 0)
            return;

        var byRecipient = new Dictionary<Guid, List<(Employee Employee, AttendanceRecord Record)>>();
        foreach (var record in legalEntityLateRecords)
        {
            var employee = employeesById[record.EmployeeId];
            var route = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                employee.Id, legalEntity.Id, AttendanceReadPermission,
                EmployeeAuthorityPurpose.AttendanceLateNotification), ct);

            if (!route.IsSuccess || route.Value is null)
            {
                _logger.LogWarning(
                    "No approver resolved for late clock-in employee {EmployeeId} in legal entity {LegalEntityId}; skipping.",
                    employee.Id, legalEntity.Id);
                continue;
            }

            if (!byRecipient.TryGetValue(route.Value.ApproverUserId, out var list))
                byRecipient[route.Value.ApproverUserId] = list = new List<(Employee, AttendanceRecord)>();
            list.Add((employee, record));
        }

        if (byRecipient.Count == 0)
            return;

        var template = await notifications.GetTemplateByCodeAsync(TemplateCode, ct);
        if (template is null)
            throw new InvalidOperationException($"Notification template '{TemplateCode}' is not seeded.");

        var relatedEntityId = BuildRelatedEntityId(legalEntity.Id, workDate);

        await unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
        {
            var sentCount = 0;
            foreach (var (recipientUserId, lateForRecipient) in byRecipient)
            {
                var alreadySent = await notifications.ExistsAsync(
                    tenantId, recipientUserId, TemplateCode, RelatedEntityType, relatedEntityId, transactionCt);
                if (alreadySent)
                    continue;

                var lateEmployeeLines = lateForRecipient
                    .Select(x => $"{x.Employee.FirstName} {x.Employee.LastName} ({x.Record.LateMinutes} min)");
                var placeholders = new Dictionary<string, string>
                {
                    ["lateCount"] = lateForRecipient.Count.ToString(),
                    ["lateEmployees"] = string.Join(", ", lateEmployeeLines),
                    ["date"] = workDate.ToString("yyyy-MM-dd"),
                };

                await dispatcher.SendTemplatedAsync(
                    tenantId, recipientUserId, TemplateCode, placeholders,
                    RelatedEntityType, relatedEntityId, transactionCt);
                sentCount++;
            }

            if (sentCount > 0)
                await unitOfWork.SaveChangesAsync(transactionCt);

            return sentCount;
        }, ct);

        _logger.LogInformation(
            "Late clock-in daily summary processed. TenantId={TenantId} LegalEntityId={LegalEntityId} Date={Date} LateEmployees={LateCount} Recipients={RecipientCount}",
            tenantId, legalEntity.Id, workDate, legalEntityLateRecords.Count, byRecipient.Count);
    }

    /// <summary>Deterministic per (legal entity, work date) id used as the notification's
    /// RelatedEntityId, so ExistsAsync can detect "already sent today" without a time-range query
    /// that would need timezone-boundary handling. Not cryptographic - just a stable mix.</summary>
    public static Guid BuildRelatedEntityId(Guid legalEntityId, DateOnly workDate)
    {
        var bytes = legalEntityId.ToByteArray();
        var dayNumberBytes = BitConverter.GetBytes(workDate.DayNumber);
        for (var i = 0; i < dayNumberBytes.Length; i++)
            bytes[i] ^= dayNumberBytes[i];
        return new Guid(bytes);
    }
}
