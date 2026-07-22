using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Storage.File;

public sealed class EfFileRecordRepository : IFileRecordRepository
{
    private readonly ApplicationDbContext _db;

    public EfFileRecordRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<FileRecord?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return _db.FileRecords.FirstOrDefaultAsync(f => f.TenantId == tenantId && f.Id == id, ct);
    }

    public async Task AddAsync(FileRecord fileRecord, CancellationToken ct = default)
    {
        await _db.FileRecords.AddAsync(fileRecord, ct);
    }
}
