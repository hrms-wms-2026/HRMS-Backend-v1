using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiOrgStructureSeeder
{
    private static async Task<Dictionary<string, Guid>> SeedDepartmentsAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var idByCode = new Dictionary<string, Guid>();

        foreach (var def in DapiOrgStructureData.Departments)
        {
            var id = DeterministicGuid($"dapi-org:department:{def.Code}");
            idByCode[def.Code] = id;

            var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (department is null)
            {
                db.Departments.Add(new Department
                {
                    Id = id,
                    TenantId = DapiTenantId,
                    LegalEntityId = DapiLegalEntityId,
                    Name = def.Name,
                    Code = def.Code,
                    IsActive = true,
                    CreatedAt = now
                });
                continue;
            }

            department.Name = def.Name;
            department.Code = def.Code;
            department.IsActive = true;
            department.UpdatedAt = now;
        }

        return idByCode;
    }

    private static async Task<Dictionary<string, Guid>> SeedPositionsAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> departmentIdByCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var idByCode = new Dictionary<string, Guid>();
        foreach (var def in DapiOrgStructureData.Positions)
        {
            idByCode[def.Code] = DeterministicGuid($"dapi-org:position:{def.Code}");
        }

        foreach (var def in DapiOrgStructureData.Positions)
        {
            var id = idByCode[def.Code];
            var departmentId = departmentIdByCode[def.DepartmentCode];
            var reportsToId = def.ReportsToPositionCode is { } reportsToCode
                ? idByCode[reportsToCode]
                : (Guid?)null;

            var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == id, ct);
            var isNew = position is null;
            if (position is null)
            {
                position = new Position
                {
                    Id = id,
                    TenantId = DapiTenantId,
                    LegalEntityId = DapiLegalEntityId,
                    DepartmentId = departmentId,
                    CreatedAt = now,
                    CreatedById = DapiOwnerUserId
                };
                db.Positions.Add(position);
            }

            position.Name = def.Name;
            position.Code = def.Code;
            position.DepartmentId = departmentId;
            position.PositionType = def.MaxOccupancy == 1 ? Position.TypeUnique : Position.TypePooled;
            position.MaxOccupancy = def.MaxOccupancy;
            position.ReportsToPositionId = reportsToId;
            position.IsActive = true;
            if (!isNew)
            {
                position.UpdatedAt = now;
            }

            // Mirrors CreatePositionCommandHandler's side effects (reporting-history +
            // coverage-record rows) since this seeder writes Position rows directly via EF
            // instead of going through that command handler.
            await SeedPositionReportingHistoryAsync(db, id, reportsToId, now, ct);
            if (reportsToId is { } ownerPositionId)
            {
                await SeedManagementCoverageRecordAsync(db, def.Code, ownerPositionId, id, now, ct);
            }
        }

        return idByCode;
    }

    private static async Task SeedPositionReportingHistoryAsync(
        ApplicationDbContext db,
        Guid positionId,
        Guid? reportsToPositionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var id = DeterministicGuid($"dapi-org:reporting-history:{positionId}");
        var history = await db.PositionReportingHistories.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (history is null)
        {
            db.PositionReportingHistories.Add(new PositionReportingHistory
            {
                Id = id,
                TenantId = DapiTenantId,
                PositionId = positionId,
                ReportsToPositionId = reportsToPositionId,
                EffectiveFrom = DateOnly.FromDateTime(now.UtcDateTime.Date),
                EffectiveTo = null,
                CreatedAt = now,
                CreatedByUserId = DapiOwnerUserId
            });
            return;
        }

        history.ReportsToPositionId = reportsToPositionId;
    }

    private static async Task SeedManagementCoverageRecordAsync(
        ApplicationDbContext db,
        string positionCode,
        Guid ownerPositionId,
        Guid coveredPositionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var id = DeterministicGuid($"dapi-org:coverage:{positionCode}");
        var record = await db.ManagementCoverageRecords.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (record is not null)
        {
            return;
        }

        db.ManagementCoverageRecords.Add(new ManagementCoverageRecord
        {
            Id = id,
            TenantId = DapiTenantId,
            LegalEntityId = DapiLegalEntityId,
            OwnerPositionId = ownerPositionId,
            CoveredTargetType = ManagementCoverageRecord.TargetPosition,
            CoveredPositionId = coveredPositionId,
            OwnerOrder = 1,
            Source = ManagementCoverageRecord.SourceReportingStructure,
            IsLocked = true,
            Status = ManagementCoverageRecord.StatusActive,
            CreatedAt = now
        });
    }

    private static async Task SeedPositionAssignmentAsync(
        ApplicationDbContext db,
        string assignmentKey,
        Guid employeeId,
        Guid positionId,
        DateOnly effectiveFrom,
        CancellationToken ct)
    {
        var id = DeterministicGuid($"dapi-org:assignment:{assignmentKey}");
        var existing = await db.PositionAssignments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (existing is not null)
        {
            existing.PositionId = positionId;
            existing.AssignmentStatus = PositionAssignmentStatus.Active;
            return;
        }

        db.PositionAssignments.Add(new PositionAssignment
        {
            Id = id,
            TenantId = DapiTenantId,
            EmployeeId = employeeId,
            PositionId = positionId,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            EffectiveFrom = effectiveFrom,
            AssignmentStatus = PositionAssignmentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = DapiOwnerUserId
        });
    }
}
