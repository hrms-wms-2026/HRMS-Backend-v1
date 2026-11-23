using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Inbox;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.SharedPlatform.Jobs;

namespace ONEVO.Infrastructure.Services.Inbox;

/// <summary>Result of one consistency pass.</summary>
public sealed record InboxEntryCheckResult(int Checked, int Missing, int Different, int Orphaned, int Repaired)
{
    public int Differences => Missing + Different + Orphaned;
}

/// <summary>
/// Task 37: once a day, compares every leave request with its inbox_items header (status and who must act) and logs the
/// differences. With <c>Inbox:EntryCheck:Repair</c> (default true) it also writes the expected header, which is how
/// existing requests are backfilled on the first run and how rows written outside the leave commands (seeders, manual
/// SQL) are corrected. A difference on a request changed through the app means a write path does not stage its header.
/// </summary>
public sealed class InboxEntryConsistencyJob : BackgroundService
{
    public const string RepairKey = "Inbox:EntryCheck:Repair";
    private const int SaveEvery = 200;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    public static readonly JobSchedule Schedule = JobSchedule.Daily("inbox-entry-consistency", TimeSpan.FromMinutes(30));

    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InboxEntryConsistencyJob> _logger;

    public InboxEntryConsistencyJob(IServiceProvider services, IConfiguration configuration, ILogger<InboxEntryConsistencyJob> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            do
            {
                try
                {
                    await _services.RunExclusiveAsync(Schedule, async ct => (int?)(await RunOnceAsync(ct)).Differences, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "InboxEntryConsistencyJob encountered an error.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown while waiting for the next tick (see MonitoringRetentionPurgeJob).
        }
    }

    /// <summary>One pass over every active tenant. Public for tests and manual runs.</summary>
    public async Task<InboxEntryCheckResult> RunOnceAsync(CancellationToken ct)
    {
        var repair = _configuration.GetValue(RepairKey, defaultValue: true);
        await using var scope = _services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var tenants = provider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = provider.GetRequiredService<ITenantContextSwitcher>();
        var tenantContext = provider.GetRequiredService<IWritableTenantContext>();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var approvals = provider.GetRequiredService<ILeaveApprovalRepository>();
        var store = provider.GetRequiredService<IInboxEntryStore>();

        // Both tables are under row-level security: the tenant list needs admin mode, each tenant's rows its own context,
        // and each tenant is saved on its own (one save across tenants fails the policy's WITH CHECK).
        tenantContext.SetAdminMode();
        var total = new InboxEntryCheckResult(0, 0, 0, 0, 0);
        foreach (var tenant in await tenants.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
                var result = await CheckTenantAsync(db, approvals, store, tenant.Id, repair, ct);
                total = new InboxEntryCheckResult(total.Checked + result.Checked, total.Missing + result.Missing,
                    total.Different + result.Different, total.Orphaned + result.Orphaned, total.Repaired + result.Repaired);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                _logger.LogError(ex, "Inbox entry check failed for tenant {TenantId}; skipping.", tenant.Id);
            }
        }

        if (total.Differences > 0)
            _logger.LogWarning(
                "Inbox entry check: {Differences} differences in {Checked} leave requests ({Missing} missing, {Different} different, {Orphaned} orphaned); {Repaired} repaired.",
                total.Differences, total.Checked, total.Missing, total.Different, total.Orphaned, total.Repaired);
        else
            _logger.LogInformation("Inbox entry check: {Checked} leave requests, no differences.", total.Checked);
        return total;
    }

    private async Task<InboxEntryCheckResult> CheckTenantAsync(
        ApplicationDbContext db, ILeaveApprovalRepository approvals, IInboxEntryStore store, Guid tenantId, bool repair, CancellationToken ct)
    {
        var requestIds = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId).OrderBy(r => r.Id).Select(r => r.Id).ToListAsync(ct);
        int missing = 0, different = 0, repaired = 0, staged = 0;
        foreach (var requestId in requestIds)
        {
            var state = await approvals.GetStateAsync(tenantId, requestId, ct);
            if (state is null)
                continue; // Employee or leave type gone: the inbox does not show it either.
            var expected = LeaveInboxEntrySync.Build(state);
            var stored = await store.GetAsync(tenantId, expected.Source, expected.Kind, requestId, ct);
            var problem = stored is null ? "missing" : Describe(expected, stored);
            if (problem is null)
                continue;

            if (stored is null) missing++; else different++;
            _logger.LogWarning("Inbox entry {Source}/{Kind} {SubjectId} in tenant {TenantId}: {Problem}.",
                expected.Source, expected.Kind, requestId, tenantId, problem);
            if (!repair)
                continue;
            await store.StageAsync(expected, ct);
            repaired++;
            if (++staged % SaveEvery == 0)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
        }
        if (repair && staged % SaveEvery != 0)
            await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // Headers whose leave request no longer exists.
        var orphans = await db.InboxEntries
            .Where(e => e.TenantId == tenantId && e.Source == LeaveInboxKinds.Source && e.Kind == LeaveInboxKinds.Request
                && !db.LeaveRequests.Any(r => r.TenantId == tenantId && r.Id == e.SubjectId))
            .ToListAsync(ct);
        foreach (var orphan in orphans)
            _logger.LogWarning("Inbox entry {Source}/{Kind} {SubjectId} in tenant {TenantId}: its leave request no longer exists.",
                orphan.Source, orphan.Kind, orphan.SubjectId, tenantId);
        if (repair && orphans.Count > 0)
        {
            db.InboxEntries.RemoveRange(orphans);
            await db.SaveChangesAsync(ct);
            repaired += orphans.Count;
        }
        db.ChangeTracker.Clear();

        return new InboxEntryCheckResult(requestIds.Count, missing, different, orphans.Count, repaired);
    }

    /// <summary>Null when the stored header matches on what the inbox relies on: status and who must act (and who is in
    /// the chain, with each person's state). Titles and timestamps are display data and not compared.</summary>
    internal static string? Describe(InboxEntryState expected, InboxEntryState stored)
    {
        if (expected.Status != stored.Status)
            return $"status {stored.Status}, expected {expected.Status}";
        static string Chain(InboxEntryState s) => string.Join(", ",
            s.Assignees.OrderBy(a => a.EmployeeId).Select(a => $"{a.EmployeeId}:{a.Status}"));
        var want = Chain(expected);
        var have = Chain(stored);
        return want == have ? null : $"assignees [{have}], expected [{want}]";
    }
}
