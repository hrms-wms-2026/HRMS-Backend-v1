using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;

public sealed class EfUserIntegrationConnectionRepository : IUserIntegrationConnectionRepository
{
    private readonly ApplicationDbContext _db;

    public EfUserIntegrationConnectionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<UserIntegrationConnection?> GetActiveAsync(
        Guid tenantId,
        Guid userId,
        string integrationKey,
        CancellationToken ct)
    {
        return _db.UserIntegrationConnections.FirstOrDefaultAsync(
            connection =>
                connection.TenantId == tenantId &&
                connection.UserId == userId &&
                connection.IntegrationKey == integrationKey &&
                connection.DisconnectedAt == null,
            ct);
    }

    public async Task AddAsync(UserIntegrationConnection connection, CancellationToken ct)
    {
        await _db.UserIntegrationConnections.AddAsync(connection, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }
}
