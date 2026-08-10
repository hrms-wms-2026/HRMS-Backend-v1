using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Screenshots;

public class EfAgentCommandRepository : IAgentCommandRepository
{
    private readonly ApplicationDbContext _db;

    public EfAgentCommandRepository(ApplicationDbContext db) => _db = db;

    public void Add(AgentCommand command)
        => _db.AgentCommands.Add(command);

    public Task<AgentCommand?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct)
        => _db.AgentCommands
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    public Task<List<AgentCommand>> GetPendingForDeviceAsync(Guid deviceId, CancellationToken ct)
        => _db.AgentCommands
            .Where(c => c.AgentDeviceId == deviceId
                        && c.Status == "pending"
                        && c.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

    public Task<int> ExpireStaleCommandsAsync(DateTimeOffset now, CancellationToken ct)
        => _db.AgentCommands
            .Where(c => c.Status == "pending" && c.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "expired"), ct);
}
