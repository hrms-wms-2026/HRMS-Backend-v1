using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectMemberRepository : IProjectMemberRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectMemberRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ProjectMember member, CancellationToken ct = default)
    {
        await _db.ProjectMembers.AddAsync(member, ct);
    }
}
