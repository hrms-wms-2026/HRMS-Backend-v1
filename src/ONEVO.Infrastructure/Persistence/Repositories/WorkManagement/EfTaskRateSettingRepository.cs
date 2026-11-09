using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public sealed class EfTaskRateSettingRepository(ApplicationDbContext db) : ITaskRateSettingRepository
{
    public Task<TaskRateSetting?> GetForTenantAsync(Guid tenantId, CancellationToken ct = default)
        => db.TaskRateSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId && !s.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<TaskRateSetting> UpsertAsync(Guid tenantId, decimal maxTaskRate, Guid? updatedByEmployeeId, CancellationToken ct = default)
    {
        var value = TaskRateLimit.Resolve(maxTaskRate);
        var row = await db.TaskRateSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId && !s.IsDeleted, ct);
        if (row is null)
        {
            row = new TaskRateSetting { Id = Guid.NewGuid(), TenantId = tenantId, MaxTaskRate = value, UpdatedByEmployeeId = updatedByEmployeeId };
            await db.TaskRateSettings.AddAsync(row, ct);
            return row;
        }

        row.MaxTaskRate = value;
        row.UpdatedByEmployeeId = updatedByEmployeeId;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        return row;
    }
}
