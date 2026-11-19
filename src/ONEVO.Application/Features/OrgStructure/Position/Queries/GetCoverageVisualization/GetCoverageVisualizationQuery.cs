using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetCoverageVisualization;

public sealed record GetCoverageVisualizationQuery(Guid LegalEntityId, Guid PositionId)
    : IRequest<Result<CoverageVisualizationResponse>>;

public sealed class GetCoverageVisualizationQueryHandler
    : IRequestHandler<GetCoverageVisualizationQuery, Result<CoverageVisualizationResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IPositionAssignmentRepository _assignments;
    private readonly IEmployeeHierarchyClosureRepository _hierarchy;
    private readonly IEmployeeRepository _employees;
    private readonly ICurrentUser _currentUser;

    public GetCoverageVisualizationQueryHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        IPositionAssignmentRepository assignments,
        IEmployeeHierarchyClosureRepository hierarchy,
        IEmployeeRepository employees,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _assignments = assignments;
        _hierarchy = hierarchy;
        _employees = employees;
        _currentUser = currentUser;
    }

    public async Task<Result<CoverageVisualizationResponse>> Handle(
        GetCoverageVisualizationQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<CoverageVisualizationResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<CoverageVisualizationResponse>.Forbidden("Tenant context missing.");

        if (await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct) is null)
            return Result<CoverageVisualizationResponse>.NotFound("Legal entity not found.");

        var owner = await _positions.GetByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.PositionId, ct);
        if (owner is null)
            return Result<CoverageVisualizationResponse>.NotFound("Position not found.");

        var allPositions = (await _positions.ListByLegalEntityAsync(
                tenantId, request.LegalEntityId, includeInactive: false, ct: ct))
            .ToList();
        var activeRecords = (await _positions.ListCoverageByOwnerPositionAsync(
                tenantId, request.LegalEntityId, request.PositionId, ct))
            .Where(record => record.Status == ManagementCoverageRecord.StatusActive)
            .OrderBy(record => record.OwnerOrder)
            .ThenBy(record => record.Id)
            .ToList();
        var ownerHolders = await _assignments.GetActiveHoldersAsync(tenantId, request.PositionId, ct);

        var rules = new List<CoverageVisualizationRuleResponse>(activeRecords.Count);
        foreach (var record in activeRecords)
        {
            var resolvedHolder = ownerHolders.Count switch
            {
                1 => ownerHolders[0],
                > 1 when record.ResponsibleEmployeeId is { } selectedId =>
                    ownerHolders.FirstOrDefault(holder => holder.EmployeeId == selectedId),
                _ => null
            };
            var resolutionReason = resolvedHolder is not null
                ? null
                : ownerHolders.Count == 0
                    ? "The owner position has no active holder."
                    : ownerHolders.Count == 1
                        ? "The active holder could not be resolved."
                        : record.ResponsibleEmployeeId is null
                            ? "Multiple people hold the owner position; choose a responsible person."
                            : "The selected responsible person no longer holds the owner position.";

            var coveredPositionIds = new HashSet<Guid>();
            var coveredEmployeeIds = new HashSet<Guid>();

            if (record.CoveredTargetType == ManagementCoverageRecord.TargetPosition
                && record.CoveredPositionId is { } coveredPositionId)
            {
                AddPositionSubtree(coveredPositionId, allPositions, coveredPositionIds);
                var targetHolders = await _assignments.GetActiveHoldersAsync(tenantId, coveredPositionId, ct);
                var targetHolderIds = targetHolders.Select(holder => holder.EmployeeId).ToList();
                coveredEmployeeIds.UnionWith(targetHolderIds);
                if (targetHolderIds.Count > 0)
                {
                    coveredEmployeeIds.UnionWith(await _hierarchy.GetDescendantEmployeeIdsAsync(
                        tenantId, targetHolderIds, ct));
                }
            }
            else if (record.CoveredTargetType == ManagementCoverageRecord.TargetDepartment
                     && record.CoveredDepartmentId is { } coveredDepartmentId)
            {
                var departmentIds = new HashSet<Guid> { coveredDepartmentId };
                departmentIds.UnionWith(await _departments.GetDescendantDepartmentIdsAsync(
                    tenantId, request.LegalEntityId, new[] { coveredDepartmentId }, ct));
                coveredPositionIds.UnionWith(allPositions
                    .Where(position => position.DepartmentId is { } departmentId && departmentIds.Contains(departmentId))
                    .Select(position => position.Id));
                coveredEmployeeIds.UnionWith(await _employees.ListActiveEmployeeIdsAsync(
                    tenantId, request.LegalEntityId, departmentIds, ct));
            }
            else if (record.CoveredTargetType == ManagementCoverageRecord.TargetCompany)
            {
                coveredPositionIds.UnionWith(allPositions.Select(position => position.Id));
                coveredEmployeeIds.UnionWith(await _employees.ListActiveEmployeeIdsAsync(
                    tenantId, request.LegalEntityId, departmentIds: null, ct));
            }

            // Every outward-facing employee id passes the same active + legal-entity filter used
            // by the authority resolver. This prevents stale closure rows from leaking into UI.
            var safeEmployeeIds = coveredEmployeeIds.Count == 0
                ? Array.Empty<Guid>()
                : (await _employees.ListActiveEmployeeIdsByIdsAsync(
                    tenantId, request.LegalEntityId, coveredEmployeeIds, ct)).ToArray();

            rules.Add(new CoverageVisualizationRuleResponse(
                record.Id,
                record.CoveredTargetType,
                record.CoveredPositionId,
                record.CoveredDepartmentId,
                record.OwnerOrder,
                record.Source,
                record.IsLocked,
                resolvedHolder is null ? "Unresolved" : "Resolved",
                resolutionReason,
                resolvedHolder?.EmployeeId,
                coveredPositionIds.OrderBy(id => id).ToArray(),
                safeEmployeeIds.OrderBy(id => id).ToArray()));
        }

        var response = new CoverageVisualizationResponse(
            request.PositionId,
            rules.SelectMany(rule => rule.CoveredPositionIds).Distinct().Count(),
            rules.SelectMany(rule => rule.CoveredEmployeeIds).Distinct().Count(),
            rules.Count(rule => rule.ResolutionStatus == "Unresolved"),
            rules);
        return Result<CoverageVisualizationResponse>.Success(response);
    }

    private static void AddPositionSubtree(
        Guid rootId,
        IReadOnlyList<Position> positions,
        HashSet<Guid> result)
    {
        var pending = new Stack<Guid>();
        pending.Push(rootId);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!result.Add(current))
                continue;
            foreach (var child in positions.Where(position => position.ReportsToPositionId == current))
                pending.Push(child.Id);
        }
    }
}
