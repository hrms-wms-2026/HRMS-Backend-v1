using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.OutboxHandlers;

/// <summary>Payload for the password reset email outbox message.</summary>
public sealed record PasswordResetEmailPayload(
    Guid TenantId,
    Guid UserId,
    string Email,
    string RawToken,
    string? TenantSlug);

/// <summary>
/// Sends the password reset email from the outbox. Safe to retry: resending the same
/// reset link is harmless and the token stays valid until it expires or is used.
/// </summary>
public sealed class PasswordResetEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public PasswordResetEmailOutboxHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public string Type => OutboxMessageTypes.PasswordResetEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PasswordResetEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("password_reset_email payload is empty.");

        await _emailService.SendPasswordResetAsync(payload.Email, payload.RawToken, payload.TenantSlug, ct);
    }
}
