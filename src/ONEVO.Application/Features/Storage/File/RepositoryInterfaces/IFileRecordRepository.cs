using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Application.Features.Storage.File.RepositoryInterfaces;

public interface IFileRecordRepository
{
    Task<FileRecord?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task AddAsync(FileRecord fileRecord, CancellationToken ct = default);
}
