using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Tests.Unit.Fakes;

public sealed class FakeFileRecordRepository : IFileRecordRepository
{
    private readonly Dictionary<Guid, FileRecord> _records = new();

    public Task<FileRecord?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        if (_records.TryGetValue(id, out var record) && record.TenantId == tenantId)
        {
            return Task.FromResult<FileRecord?>(record);
        }

        return Task.FromResult<FileRecord?>(null);
    }

    public Task AddAsync(FileRecord fileRecord, CancellationToken ct = default)
    {
        _records[fileRecord.Id] = fileRecord;
        return Task.CompletedTask;
    }
}
