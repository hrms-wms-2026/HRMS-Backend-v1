using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.ActivityMonitoring;

public class EfActivityRawBufferRepository : IActivityRawBufferRepository
{
    private readonly ApplicationDbContext _db;

    public EfActivityRawBufferRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ActivityRawBuffer buffer, CancellationToken ct)
        => await _db.ActivityRawBuffers.AddAsync(buffer, ct);
}
