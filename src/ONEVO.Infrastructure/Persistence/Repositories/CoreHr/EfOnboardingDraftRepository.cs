using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public class EfOnboardingDraftRepository : IOnboardingDraftRepository
{
    private readonly ApplicationDbContext _db;

    public EfOnboardingDraftRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<OnboardingDraftEntity?> GetTrackedAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.OnboardingDrafts.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, ct);

    // The projection to OnboardingDraftResponse (including EF.Property<uint>(d, "xmin")) is
    // inlined directly in each Select() rather than delegated to a shared static method - a
    // method call inside a Select() lambda does not translate to SQL (see the EfEmployeeRepository
    // doc comment for the same lesson learned on a different query).
    public async Task<OnboardingDraftResponse?> GetResponseByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.OnboardingDrafts.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Id == id)
            .Select(d => new OnboardingDraftResponse(
                d.Id,
                d.EmployeeName,
                d.WorkEmail,
                d.LegalEntityId,
                d.DepartmentId,
                d.PositionId,
                d.EmploymentType,
                d.StartDate,
                d.EmployeeNumber,
                d.ScheduleId,
                d.SelectedTemplateId,
                d.EditedTasksJson,
                d.Status,
                d.DraftReason,
                d.LastSavedStep,
                d.StartedById,
                EF.Property<uint>(d, "xmin").ToString()))
            .FirstOrDefaultAsync(ct);

    public async Task<(IReadOnlyList<OnboardingDraftResponse> Items, int TotalCount)> ListAsync(
        Guid tenantId, Guid? startedById, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.OnboardingDrafts.AsNoTracking().Where(d => d.TenantId == tenantId);

        if (startedById is not null)
        {
            query = query.Where(d => d.StartedById == startedById.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt).ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new OnboardingDraftResponse(
                d.Id,
                d.EmployeeName,
                d.WorkEmail,
                d.LegalEntityId,
                d.DepartmentId,
                d.PositionId,
                d.EmploymentType,
                d.StartDate,
                d.EmployeeNumber,
                d.ScheduleId,
                d.SelectedTemplateId,
                d.EditedTasksJson,
                d.Status,
                d.DraftReason,
                d.LastSavedStep,
                d.StartedById,
                EF.Property<uint>(d, "xmin").ToString()))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<DraftListItemResponse> Items, int TotalCount)> ListWithNamesAsync(
        Guid tenantId, Guid? startedById, int page, int pageSize, CancellationToken ct = default)
    {
        var joined =
            from d in _db.OnboardingDrafts.AsNoTracking()
            where d.TenantId == tenantId
            join position in _db.Positions.AsNoTracking() on d.PositionId equals position.Id into posJoin
            from position in posJoin.DefaultIfEmpty()
            join dept in _db.Departments.AsNoTracking() on d.DepartmentId equals dept.Id into deptJoin
            from dept in deptJoin.DefaultIfEmpty()
            join starter in _db.Users.AsNoTracking() on d.StartedById equals starter.Id into starterJoin
            from starter in starterJoin.DefaultIfEmpty()
            select new { d, position, dept, starter };

        if (startedById is not null)
        {
            joined = joined.Where(row => row.d.StartedById == startedById.Value);
        }

        var totalCount = await joined.CountAsync(ct);

        var items = await joined
            .OrderByDescending(row => row.d.UpdatedAt ?? row.d.CreatedAt).ThenBy(row => row.d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new DraftListItemResponse(
                row.d.Id,
                row.d.EmployeeName,
                row.position != null ? row.position.Name : null,
                row.dept != null ? row.dept.Name : null,
                row.d.Status,
                row.d.DraftReason,
                row.d.LastSavedStep,
                row.starter != null ? row.starter.FirstName + " " + row.starter.LastName : "Unknown"))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task AddAsync(OnboardingDraftEntity draft, CancellationToken ct = default)
        => await _db.OnboardingDrafts.AddAsync(draft, ct);

    public void SetExpectedVersion(OnboardingDraftEntity draft, string expectedVersion)
    {
        if (!uint.TryParse(expectedVersion, out var expectedXmin))
        {
            return;
        }

        _db.Entry(draft).Property("xmin").OriginalValue = expectedXmin;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
    }
}
