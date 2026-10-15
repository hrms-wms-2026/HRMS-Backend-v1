using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Services;

public interface IReportingLineMoveService
{
    Task<Result<ReportingLineMoveImpactResponse>> PreviewAsync(
        Guid tenantId,
        Guid legalEntityId,
        IReadOnlyCollection<Guid> selectedEmployeeIds,
        Guid newManagerEmployeeId,
        CancellationToken ct = default);

    Task<Result<ReportingLineMoveImpactResponse>> MoveAsync(
        Guid tenantId,
        Guid legalEntityId,
        IReadOnlyCollection<Guid> selectedEmployeeIds,
        Guid newManagerEmployeeId,
        string? reason,
        CancellationToken ct = default);
}
