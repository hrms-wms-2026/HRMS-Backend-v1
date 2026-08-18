using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Services.CoreHr.BulkOnboarding;

/// <summary>
/// Polls for batches in draft_creation_pending or finalize_pending and drives
/// IOnboardingDraftWriteService per row - same BackgroundService/PeriodicTimer shape as
/// OutboxProcessor, no MediatR (see spec §6.1 and plan Task 4 for why).
/// </summary>
public sealed class BulkOnboardingBatchProcessor : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly IServiceProvider _services;
    private readonly ILogger<BulkOnboardingBatchProcessor> _logger;

    public BulkOnboardingBatchProcessor(IServiceProvider services, ILogger<BulkOnboardingBatchProcessor> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bulk onboarding batch processing iteration failed; will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Public so integration tests can drive one iteration synchronously without
    /// waiting on the poll timer - same precedent as ActivityDailySummaryJob.RunAggregationAsync.</summary>
    public async Task ProcessOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var batchRepository = scope.ServiceProvider.GetRequiredService<IBulkOnboardingBatchRepository>();

        // Admin mode before the cross-tenant scan: bulk_onboarding_batches/_rows use the
        // mode-aware RLS policy from Task 2, which only bypasses tenant scoping in admin mode -
        // without this, GetOldestPendingAsync silently returns nothing forever (see Task 2/3).
        var writableTenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        writableTenantContext.SetAdminMode();

        var batch = await batchRepository.GetOldestPendingAsync(BulkOnboardingBatchStatus.DraftCreationPending, ct);
        if (batch is null)
            return;

        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        var writeService = scope.ServiceProvider.GetRequiredService<IOnboardingDraftWriteService>();

        var tenant = await tenantRepository.GetByIdAsync(batch.TenantId, ct);
        if (tenant is null)
        {
            _logger.LogError("Bulk onboarding batch {BatchId} references missing tenant {TenantId}.", batch.Id, batch.TenantId);
            return;
        }
        await tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var rows = await batchRepository.ListTrackedRowsAsync(batch.TenantId, batch.Id, ct);
        foreach (var row in rows.Where(r => r.Status == BulkOnboardingBatchRowStatus.Valid))
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawDataJson) ?? new();
            var mapping = JsonSerializer.Deserialize<Dictionary<string, string?>>(batch.ColumnMappingJson ?? "{}") ?? new();
            string? Get(string field) => mapping.TryGetValue(field, out var col) && col is not null && raw.TryGetValue(col, out var v) ? v : null;

            var command = new SaveOnboardingDraftCommand(
                DraftId: null,
                FirstName: Get("firstName") ?? string.Empty,
                LastName: Get("lastName") ?? string.Empty,
                WorkEmail: Get("workEmail") ?? string.Empty,
                LegalEntityId: batch.LegalEntityId,
                DepartmentId: row.ResolvedDepartmentId,
                PositionId: row.ResolvedPositionId,
                EmploymentType: Get("employmentType") ?? batch.DefaultEmploymentType ?? string.Empty,
                StartDate: DateOnly.TryParse(Get("startDate"), out var startDate) ? startDate : default,
                EmployeeNumber: Get("employeeNumber"),
                WorkModeId: batch.DefaultWorkModeId ?? 0,
                SelectedTemplateId: row.ResolvedTemplateId,
                EditedTasksJson: null,
                LastSavedStep: OnboardingWizardStep.ReviewAndSubmit,
                IfMatchVersion: null);

            var result = await writeService.SaveAsync(batch.TenantId, batch.CreatedByUserId, command, ct);
            if (result.IsSuccess)
            {
                row.Status = BulkOnboardingBatchRowStatus.DraftCreated;
                row.OnboardingDraftId = result.Value!.Id;
                row.ErrorMessage = null;
            }
            else
            {
                row.Status = BulkOnboardingBatchRowStatus.DraftFailed;
                row.ErrorMessage = result.Error;
            }
        }

        batch.Status = BulkOnboardingBatchStatus.DraftsCreated;
        batch.CompletedAt = null; // reserved for the finalize leg's completion, not draft creation
        await batchRepository.SaveChangesAsync(ct);
    }
}
