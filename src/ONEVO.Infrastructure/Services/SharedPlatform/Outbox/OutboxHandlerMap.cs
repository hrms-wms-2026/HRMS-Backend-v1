using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Outbox;

/// <summary>
/// Task 31: the outbox message type to handler lookup. Two handlers for one type would leave it to registration
/// order which one runs, so that is refused with both handler classes named. Used by <see cref="OutboxProcessor"/>
/// for every batch and by <see cref="OutboxHandlerStartupCheck"/> once at start-up.
/// </summary>
public static class OutboxHandlerMap
{
    public static IReadOnlyDictionary<string, IOutboxMessageHandler> Build(IEnumerable<IOutboxMessageHandler> handlers)
    {
        var map = new Dictionary<string, IOutboxMessageHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (string.IsNullOrWhiteSpace(handler.Type))
                throw new InvalidOperationException(
                    $"Outbox handler {handler.GetType().FullName} has no message type.");

            if (!map.TryAdd(handler.Type, handler))
                throw new InvalidOperationException(
                    $"Outbox message type '{handler.Type}' has two handlers: {map[handler.Type].GetType().FullName} " +
                    $"and {handler.GetType().FullName}. Register exactly one handler per type.");
        }

        return map;
    }
}
