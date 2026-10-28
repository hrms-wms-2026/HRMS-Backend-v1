using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Outbox;

public sealed class OutboxWriter : IOutboxWriter
{
    private readonly ApplicationDbContext _db;
    private readonly IEncryptionService _encryption;
    private readonly IDateTimeProvider _clock;

    public OutboxWriter(ApplicationDbContext db, IEncryptionService encryption, IDateTimeProvider clock)
    {
        _db = db;
        _encryption = encryption;
        _clock = clock;
    }

    public async Task EnqueueAsync<TPayload>(
        string type,
        TPayload payload,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var payloadJson = JsonSerializer.Serialize(payload);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = type,
            EncryptedPayload = _encryption.Encrypt(payloadJson),
            TenantId = tenantId,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0,
            CreatedAt = now,
            NextAttemptAt = now
        };

        await _db.Set<OutboxMessage>().AddAsync(message, ct);
    }

    public async Task<bool> EnqueueKeyedAsync<TPayload>(
        string type,
        TPayload payload,
        string deduplicationKey,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deduplicationKey);
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "A keyed outbox message must be enqueued inside the handler's transaction (IUnitOfWork.ExecuteInTransactionAsync).");

        var now = _clock.UtcNow;
        var encryptedPayload = _encryption.Encrypt(JsonSerializer.Serialize(payload));
        var inserted = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO outbox_messages
                (id, type, encrypted_payload, tenant_id, status, attempt_count, created_at, next_attempt_at, deduplication_key)
            VALUES
                ({Guid.NewGuid()}, {type}, {encryptedPayload}, {tenantId}, {OutboxMessageStatus.Pending}, 0, {now}, {now}, {deduplicationKey})
            ON CONFLICT (deduplication_key) WHERE deduplication_key IS NOT NULL DO NOTHING
            """, ct);
        return inserted == 1;
    }
}
