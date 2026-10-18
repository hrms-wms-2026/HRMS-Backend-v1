using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Lookups;

namespace ONEVO.Infrastructure.Persistence.Repositories.Conversations;

public class EfConversationPeopleDirectory : IConversationPeopleDirectory
{
    private readonly ApplicationDbContext _db;

    public EfConversationPeopleDirectory(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> SearchActiveAsync(Guid tenantId, string? search, int take, CancellationToken ct = default)
    {
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.EmploymentStatusId == EmploymentStatusIds.Active);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e => (e.FirstName + " " + e.LastName).ToLower().Contains(term));
        }
        return await query.OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => e.Id).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
    }
}
