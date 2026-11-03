using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Lookups;

namespace ONEVO.Infrastructure.Persistence.Repositories.Conversations;

public class EfConversationPeopleDirectory : IConversationPeopleDirectory
{
    private readonly ApplicationDbContext _db;

    public EfConversationPeopleDirectory(ApplicationDbContext db) => _db = db;

    public Task<IReadOnlyList<Guid>> SearchActiveAsync(Guid tenantId, string? search, int take, CancellationToken ct = default)
        => SearchAsync(tenantId, null, search, take, ct);

    public async Task<IReadOnlyList<Guid>> SearchActiveInLegalEntityOfAsync(
        Guid tenantId, Guid employeeId, string? search, int take, CancellationToken ct = default)
    {
        var legalEntityId = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => e.LegalEntityId)
            .FirstOrDefaultAsync(ct);
        return await SearchAsync(tenantId, legalEntityId, search, take, ct);
    }

    private async Task<IReadOnlyList<Guid>> SearchAsync(Guid tenantId, Guid? legalEntityId, string? search, int take, CancellationToken ct)
    {
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.EmploymentStatusId == EmploymentStatusIds.Active);
        if (legalEntityId is { } entity)
            query = query.Where(e => e.LegalEntityId == entity);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e => (e.FirstName + " " + e.LastName).ToLower().Contains(term));
        }
        return await query.OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => e.Id).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
    }
}
