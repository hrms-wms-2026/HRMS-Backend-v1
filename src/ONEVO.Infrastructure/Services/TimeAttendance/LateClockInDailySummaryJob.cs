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
    private readonly Dictionary<Guid, DateOnly> _lastRunLocalDateByLegalEntity = new();

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
            catch (Exception ex)
            {
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

            var dueAt = shiftStart.ToTimeSpan() + OffsetFromShiftStart;
            var alreadyRunToday = _lastRunLocalDateByLegalEntity.TryGetValue(legalEntity.Id, out var lastRun)
                && lastRun == resolution.WorkDate;
            if (resolution.LocalNow.TimeOfDay < dueAt || alreadyRunToday)
                continue;

            try
            {
                await RunForLegalEntityAsync(services, tenant.Id, legalEntity, resolution.WorkDate, ct);
                // Only recorded on success: a transient failure (DB blip, one bad row) should be
                // retried on the next 15-minute tick for the rest of today, not silently skipped
                // until tomorrow. The DB-level ExistsAsync check in RunForLegalEntityAsync makes a
                // retry after partial success safe (already-sent recipients are not re-notified).
                _lastRunLocalDateByLegalEntity[legalEntity.Id] = resolution.WorkDate;
            }
            catch (Exception ex)
            {
                // One legal entity's failure must not stop the rest of this tenant's legal
                // entities (or the rest of this tenant's tick) from being processed - same
                // per-unit isolation as LeaveYearEndEntitlementJob.RunForYearAsync's per-tenant
                // try/catch.
                _logger.LogWarning(ex,
                    "Late clock-in daily summary failed for legal entity {LegalEntityId} in tenant {TenantId}; will retry next tick.",
                    legalEntity.Id, tenant.Id);
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
