using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

namespace ONEVO.Infrastructure.Services.CoreHr;

public class OnboardingWizardLookupService : IOnboardingWizardLookupService
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IPrimaryEntityAssetLinker _assetLinker;
    private readonly IPositionPeopleChartReader _peopleChart;

    public OnboardingWizardLookupService(
        ApplicationDbContext db, IFileStorageService fileStorage, IPrimaryEntityAssetLinker assetLinker,
        IPositionPeopleChartReader peopleChart)
    {
        _db = db;
        _fileStorage = fileStorage;
        _assetLinker = assetLinker;
        _peopleChart = peopleChart;
    }

    public async Task<IReadOnlyList<ReportingChainNodeResponse>> GetReportingChainAsync(
        Guid tenantId, Guid managerEmployeeId, CancellationToken ct = default)
    {
        var legalEntityId = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == managerEmployeeId)
            .Select(e => e.LegalEntityId)
            .FirstOrDefaultAsync(ct);
        if (legalEntityId is not Guid le)
            return [];

        // Same resolution as the Org > Positions "People" view, so the preview and the chart
        // always agree - including multi-holder steps (first holder) and vacant positions.
        var chart = await _peopleChart.BuildAsync(tenantId, le, ct);
        var byId = chart.ToDictionary(n => n.Id);
        if (!byId.TryGetValue($"e:{managerEmployeeId}", out var manager))
            return [];

        var line = new List<ReportingChainNodeResponse>();
        var seen = new HashSet<string>();
        for (var node = manager; node is not null && seen.Add(node.Id);
             node = node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) ? parent : null)
        {
            line.Add(new ReportingChainNodeResponse(
                node.EmployeeId, node.FirstName, node.LastName, node.PositionName, node.DepartmentName,
                node.Kind, node.ReportingLine));
        }

        line.Reverse(); // top of the organisation first, the manager last
        return line;
    }

    public async Task<OnboardingClockInDefaultsResponse> GetClockInDefaultsAsync(
        Guid tenantId, Guid legalEntityId, Guid workModeId, Guid? positionId, Guid? departmentId,
        CancellationToken ct = default)
    {
        var photoRequired = await _db.TimeAttendanceWorkModes.AsNoTracking()
            .Where(w => w.TenantId == tenantId && w.Id == workModeId)
            .Select(w => w.PhotoRequired)
            .FirstOrDefaultAsync(ct);

        // Same tier order as MonitoringToggleResolverService.ResolveForEmployeeAsync, minus the
        // employee tier (that is exactly what the wizard's toggle sets) and the role tier.
        var scopeIds = new List<Guid> { workModeId };
        if (positionId is Guid p) scopeIds.Add(p);
        if (departmentId is Guid d) scopeIds.Add(d);

        var policies = await _db.MonitoringPolicyOverrides.AsNoTracking()
            .Where(o => o.TenantId == tenantId && scopeIds.Contains(o.ScopeId))
            .ToListAsync(ct);

        bool? TierValue(string scopeType, Guid? scopeId) => scopeId is null
            ? null
            : policies
                .Where(o => o.ScopeType == scopeType && o.ScopeId == scopeId.Value)
                .Select(o => o.WorkLocationVerification)
                .FirstOrDefault(v => v.HasValue);

        var toggles = await _db.MonitoringFeatureToggles.AsNoTracking()
            .Where(t => t.TenantId == tenantId && (t.LegalEntityId == legalEntityId || t.LegalEntityId == null))
            .OrderByDescending(t => t.LegalEntityId == legalEntityId)
            .FirstOrDefaultAsync(ct);

        var locationCapture = MonitoringToggleResolution.Resolve(
            employeeOverride: null,
            workModeOverride: TierValue("work_mode", workModeId),
            rolePolicy: null,
            positionPolicy: TierValue("position", positionId),
            departmentPolicy: TierValue("department", departmentId),
            tenantToggle: toggles?.WorkLocationVerification);

        return new OnboardingClockInDefaultsResponse(photoRequired, locationCapture);
    }

    public async Task<IReadOnlyList<ManagerCandidateResponse>> SearchManagerCandidatesAsync(
        Guid tenantId, Guid legalEntityId, string search, int limit, CancellationToken ct = default)
    {
        var term = (search ?? string.Empty).Trim().ToLower();
        if (term.Length == 0)
            return [];

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await (
            from pa in _db.PositionAssignments.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on pa.EmployeeId equals e.Id
            join position in _db.Positions.AsNoTracking() on pa.PositionId equals position.Id
            join dept in _db.Departments.AsNoTracking() on e.DepartmentId equals dept.Id into deptJoin
            from dept in deptJoin.DefaultIfEmpty()
            where pa.TenantId == tenantId
                && e.TenantId == tenantId
                && e.LegalEntityId == legalEntityId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active
                && pa.EffectiveFrom <= today
                && (e.FirstName.ToLower().Contains(term)
                    || e.LastName.ToLower().Contains(term)
                    || (e.FirstName + " " + e.LastName).ToLower().Contains(term)
                    || position.Name.ToLower().Contains(term)
                    || (dept != null && dept.Name.ToLower().Contains(term)))
            orderby e.FirstName, e.LastName, e.Id
            select new ManagerCandidateResponse(
                e.Id, e.FirstName, e.LastName, position.Id, position.Name, dept != null ? dept.Name : null))
            .Take(Math.Clamp(limit, 1, 50) * 2)
            .ToListAsync(ct);

        // One row per person (latest seat wins if someone somehow holds two primaries).
        return rows
            .GroupBy(r => r.EmployeeId)
            .Select(g => g.First())
            .Take(Math.Clamp(limit, 1, 50))
            .ToList();
    }

    public async Task AddEmployeeOverrideAsync(EmployeeMonitoringOverride entity, CancellationToken ct = default) =>
        await _db.EmployeeMonitoringOverrides.AddAsync(entity, ct);

    public async Task<bool> IsUsableDraftAvatarAsync(Guid tenantId, Guid userId, Guid fileId, CancellationToken ct = default)
    {
        var record = await _fileStorage.GetRecordAsync(tenantId, fileId, ct);
        if (!record.IsSuccess || record.Value!.DeletedAt is not null || record.Value.UploadedByUserId != userId)
            return false;

        var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.EmployeeAvatar)!;
        var extension = Path.GetExtension(record.Value.OriginalFileName);
        return record.Value.FileSizeBytes <= rule.MaxSizeBytes
            && rule.AllowedContentTypes.Contains(record.Value.ContentType, StringComparer.OrdinalIgnoreCase)
            && rule.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public async Task LinkDraftAvatarAsync(Guid tenantId, Guid employeeId, Guid fileId, CancellationToken ct = default)
    {
        var record = await _fileStorage.GetRecordAsync(tenantId, fileId, ct);
        if (!record.IsSuccess || record.Value!.DeletedAt is not null)
            return;

        // The linker only links a file on behalf of whoever uploaded it. Finalize/approval can be
        // done by a different HR user than the one who picked the photo, so act as the uploader.
        await _assetLinker.LinkAsync(
            tenantId, record.Value.UploadedByUserId, EntityAssetOwnerTypes.Employee, employeeId,
            UploadPurposeCatalog.EmployeeAvatar, fileId, ct);
    }
}
