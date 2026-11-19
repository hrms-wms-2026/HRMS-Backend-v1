using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Services;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.OrgStructure;

public class PositionPeopleChartReader : IPositionPeopleChartReader
{
    private const string LinePosition = "position";
    private const string LineDefault = "default";
    private const string LineCustom = "custom";

    private readonly ApplicationDbContext _db;

    public PositionPeopleChartReader(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<PositionPeopleChartNodeResponse>> BuildAsync(
        Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
    {
        var positions = await (
            from p in _db.Positions.AsNoTracking()
            join dept in _db.Departments.AsNoTracking() on p.DepartmentId equals dept.Id into deptJoin
            from dept in deptJoin.DefaultIfEmpty()
            where p.TenantId == tenantId && p.LegalEntityId == legalEntityId && p.IsActive
            select new { p.Id, p.Name, p.ReportsToPositionId, p.MaxOccupancy, DepartmentName = dept != null ? dept.Name : null })
            .ToListAsync(ct);
        var positionById = positions.ToDictionary(p => p.Id);
        var positionIds = positionById.Keys.ToList();

        var avatarAssets = _db.EntityAssets.AsNoTracking().Where(a =>
            a.TenantId == tenantId
            && a.OwnerType == EntityAssetOwnerTypes.Employee
            && a.AssetPurpose == UploadPurposeCatalog.EmployeeAvatar
            && a.IsPrimary);

        var holders = await (
            from pa in _db.PositionAssignments.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on pa.EmployeeId equals e.Id
            join asset in avatarAssets on e.Id equals asset.OwnerId into assetJoin
            from asset in assetJoin.DefaultIfEmpty()
            join dept in _db.Departments.AsNoTracking() on e.DepartmentId equals dept.Id into deptJoin
            from dept in deptJoin.DefaultIfEmpty()
            where pa.TenantId == tenantId
                && positionIds.Contains(pa.PositionId)
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && (pa.AssignmentStatus == PositionAssignmentStatus.Active
                    || pa.AssignmentStatus == PositionAssignmentStatus.Planned)
            select new
            {
                IsPlanned = pa.AssignmentStatus == PositionAssignmentStatus.Planned,
                pa.EmployeeId,
                pa.PositionId,
                pa.ReportsToEmployeeId,
                pa.EffectiveFrom,
                e.FirstName,
                e.LastName,
                e.Email,
                AvatarFileId = asset == null ? (Guid?)null : asset.FileRecordId,
                DepartmentName = dept != null ? dept.Name : null
            })
            .ToListAsync(ct);

        // One seat per person (latest wins); holders of each position in the same stable order
        // the Add Employee wizard uses for its default pick (first name, last name, id).
        var invited = holders
            .Where(h => h.IsPlanned && !holders.Any(a => !a.IsPlanned && a.EmployeeId == h.EmployeeId))
            .GroupBy(h => h.EmployeeId)
            .Select(g => g.OrderByDescending(h => h.EffectiveFrom).First())
            .ToList();
        var seatByEmployee = holders
            .Where(h => !h.IsPlanned)
            .GroupBy(h => h.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.EffectiveFrom).First());
        var holdersByPosition = seatByEmployee.Values
            .GroupBy(h => h.PositionId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(h => h.FirstName).ThenBy(h => h.LastName).ThenBy(h => h.EmployeeId).ToList());

        // Where a card for something under position X hangs: under X's only holder, its first
        // holder (a default - nobody picked), or X's own vacant card.
        (string? ParentId, string? Line) UnderPosition(Guid? reportsToPositionId)
        {
            if (reportsToPositionId is not Guid rp || !positionById.ContainsKey(rp))
                return (null, null);
            if (holdersByPosition.TryGetValue(rp, out var aboveHolders) && aboveHolders.Count > 0)
                return ($"e:{aboveHolders[0].EmployeeId}", aboveHolders.Count == 1 ? LinePosition : LineDefault);
            return ($"p:{rp}", LinePosition);
        }

        var nodes = new List<PositionPeopleChartNodeResponse>();
        foreach (var position in positions)
        {
            holdersByPosition.TryGetValue(position.Id, out var positionHolders);
            var holderCount = positionHolders?.Count ?? 0;

            if (holderCount == 0)
            {
                var (parentId, line) = UnderPosition(position.ReportsToPositionId);
                nodes.Add(new PositionPeopleChartNodeResponse(
                    $"p:{position.Id}", parentId, "vacant", position.Id, position.Name, position.DepartmentName,
                    null, null, null, null, line, position.MaxOccupancy, 0));
                continue;
            }

            foreach (var holder in positionHolders!)
            {
                string? parentId;
                string? line;
                if (holder.ReportsToEmployeeId is Guid managerId && seatByEmployee.TryGetValue(managerId, out var managerSeat))
                {
                    // Explicit pick: in the line above ("position") or a one-off from elsewhere ("custom").
                    parentId = $"e:{managerId}";
                    line = managerSeat.PositionId == position.ReportsToPositionId ? LinePosition : LineCustom;
                }
                else
                {
                    (parentId, line) = UnderPosition(position.ReportsToPositionId);
                }

                nodes.Add(new PositionPeopleChartNodeResponse(
                    $"e:{holder.EmployeeId}", parentId, "person", position.Id, position.Name,
                    holder.DepartmentName ?? position.DepartmentName,
                    holder.EmployeeId, holder.FirstName, holder.LastName, holder.AvatarFileId,
                    line, position.MaxOccupancy, holderCount, WorkEmail: holder.Email));
            }
        }

        // Invited employees: same placement rule, never a parent (their seat isn't active yet).
        foreach (var person in invited)
        {
            if (!positionById.TryGetValue(person.PositionId, out var position))
                continue;
            string? parentId;
            string? line;
            if (person.ReportsToEmployeeId is Guid managerId && seatByEmployee.TryGetValue(managerId, out var managerSeat))
            {
                parentId = $"e:{managerId}";
                line = managerSeat.PositionId == position.ReportsToPositionId ? LinePosition : LineCustom;
            }
            else
            {
                (parentId, line) = UnderPosition(position.ReportsToPositionId);
            }

            nodes.Add(new PositionPeopleChartNodeResponse(
                $"i:{person.EmployeeId}", parentId, "person", position.Id, position.Name,
                person.DepartmentName ?? position.DepartmentName,
                person.EmployeeId, person.FirstName, person.LastName, person.AvatarFileId,
                line, position.MaxOccupancy, holdersByPosition.TryGetValue(position.Id, out var active) ? active.Count : 0,
                IsPending: true, WorkEmail: person.Email));
        }

        return BreakCycles(nodes);
    }

    /// <summary>
    /// One-off picks can form a loop (A reports to B, B to A) or point at a card that isn't on
    /// the chart. Any card whose parent chain never reaches the top is lifted to the top instead
    /// of disappearing.
    /// </summary>
    private static IReadOnlyList<PositionPeopleChartNodeResponse> BreakCycles(List<PositionPeopleChartNodeResponse> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var result = new List<PositionPeopleChartNodeResponse>(nodes.Count);
        foreach (var node in nodes)
        {
            var seen = new HashSet<string> { node.Id };
            var cursor = node.ParentId;
            var reachesTop = true;
            while (cursor is not null)
            {
                if (!seen.Add(cursor) || !byId.TryGetValue(cursor, out var parent))
                {
                    reachesTop = false;
                    break;
                }
                cursor = parent.ParentId;
            }

            result.Add(reachesTop ? node : node with { ParentId = null, ReportingLine = null });
        }

        return result;
    }
}
