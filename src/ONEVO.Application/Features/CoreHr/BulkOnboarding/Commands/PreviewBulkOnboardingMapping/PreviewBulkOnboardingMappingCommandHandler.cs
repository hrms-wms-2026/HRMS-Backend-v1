using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.PreviewBulkOnboardingMapping;

public class PreviewBulkOnboardingMappingCommandHandler
    : IRequestHandler<PreviewBulkOnboardingMappingCommand, Result<RowPreviewResult>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IWorkModeRepository _workModeRepository;
    private readonly ICurrentUser _currentUser;

    public PreviewBulkOnboardingMappingCommandHandler(
        IBulkOnboardingBatchRepository batchRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository,
        IWorkModeRepository workModeRepository,
        ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _workModeRepository = workModeRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<RowPreviewResult>> Handle(PreviewBulkOnboardingMappingCommand request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetTrackedAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<RowPreviewResult>.NotFound("The batch could not be found.");

        batch.ColumnMappingJson = JsonSerializer.Serialize(request.Mapping);
        await _batchRepository.SaveChangesAsync(ct);

        var rows = await _batchRepository.ListRowsAsync(_currentUser.TenantId, batch.Id, ct);
        var firstRow = rows.OrderBy(r => r.RowNumber).FirstOrDefault();
        if (firstRow is null)
            return Result<RowPreviewResult>.NotFound("This batch has no rows.");

        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(firstRow.RawDataJson) ?? new();
        string? Get(string field) => request.Mapping.TryGetValue(field, out var col) && col is not null && raw.TryGetValue(col, out var v) ? v : null;

        var departmentName = Get("department");
        var positionName = Get("position");
        var workModeName = Get("workMode");

        var departments = await _departmentRepository.ListByLegalEntityAsync(_currentUser.TenantId, batch.LegalEntityId, includeInactive: false, ct);
        var resolvedDepartment = departmentName is null ? null :
            departments.FirstOrDefault(d => string.Equals(d.Name, departmentName, StringComparison.OrdinalIgnoreCase));

        var positions = await _positionRepository.ListByLegalEntityAsync(_currentUser.TenantId, batch.LegalEntityId, includeInactive: false, departmentId: null, ct);
        var resolvedPosition = positionName is null ? null :
            positions.FirstOrDefault(p => string.Equals(p.Name, positionName, StringComparison.OrdinalIgnoreCase));

        var workModes = await _workModeRepository.ListActiveAsync(ct);
        var resolvedWorkMode = workModeName is null ? null :
            workModes.FirstOrDefault(w => string.Equals(w.Code, workModeName, StringComparison.OrdinalIgnoreCase));

        return Result<RowPreviewResult>.Success(new RowPreviewResult(
            Get("firstName"), Get("lastName"), Get("workEmail"), Get("startDate"), Get("employmentType"),
            resolvedWorkMode?.Code, resolvedDepartment?.Name, resolvedPosition?.Name,
            null,
            Get("employeeNumber")));
    }
}
