namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Consumer for one outbox message type. Implementations must be safe to retry:
/// the worker redelivers on failure, so duplicate handling must be harmless.
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>Message type key this handler consumes (see OutboxMessageTypes).</summary>
    string Type { get; }

    /// <summary>Handles the decrypted JSON payload of the message.</summary>
    Task HandleAsync(string payloadJson, CancellationToken ct);
}

/// <summary>Well-known outbox message type keys.</summary>
public static class OutboxMessageTypes
{
    public const string TenantOwnerInviteEmail = "tenant_owner_invite_email";
    public const string PasswordResetEmail = "password_reset_email";
    public const string AdminPasswordResetEmail = "admin_password_reset_email";
}
