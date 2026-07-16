using System.Text.Json;
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
}
