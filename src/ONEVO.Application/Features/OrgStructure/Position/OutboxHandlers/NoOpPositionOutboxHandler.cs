using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.OrgStructure.OutboxHandlers;

/// <summary>
/// Placeholder consumer for the four Position lifecycle outbox message types (position_created,
/// position_updated, position_archived, position_restored). No real downstream consumer (audit
/// log, search index, notification, ...) has been requested yet; this handler exists solely so
/// OutboxProcessor finds a registered handler for each type instead of throwing
/// InvalidOperationException, retrying 8x, and parking every position write as Failed with an
/// error-level log entry. One instance is registered per Type (see DependencyInjection.cs) rather
/// than one class per type, since all four currently do the identical (nothing) amount of work.
/// Replace the relevant registration with a real IOutboxMessageHandler when a consumer is needed.
/// </summary>
public sealed class NoOpPositionOutboxHandler : IOutboxMessageHandler
{
    public string Type { get; }

    public NoOpPositionOutboxHandler(string type)
    {
        Type = type;
    }

    public Task HandleAsync(string payloadJson, CancellationToken ct) => Task.CompletedTask;
}
