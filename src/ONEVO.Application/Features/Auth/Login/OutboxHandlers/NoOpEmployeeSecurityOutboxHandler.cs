using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.OutboxHandlers;

/// <summary>Placeholder consumer for employee_security_updated - no downstream consumer (audit
/// log, notification email, ...) has been requested yet. Mirrors NoOpPositionOutboxHandler.</summary>
public sealed class NoOpEmployeeSecurityOutboxHandler : IOutboxMessageHandler
{
    public string Type { get; }
    public NoOpEmployeeSecurityOutboxHandler(string type) => Type = type;
    public Task HandleAsync(string payloadJson, CancellationToken ct) => Task.CompletedTask;
}
