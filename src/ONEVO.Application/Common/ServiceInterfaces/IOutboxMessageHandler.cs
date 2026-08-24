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
    public const string AdminPasswordChangedEmail = "admin_password_changed_email";

    // No IOutboxMessageHandler consumer is registered for these yet - messages enqueued under
    // these types sit Pending until a handler is added, then park as Failed after MaxAttempts
    // (~8 retries, capped at 1h backoff) via OutboxProcessor. Producer-side only for now.
    public const string PositionCreated = "position_created";
    public const string PositionUpdated = "position_updated";
    public const string PositionArchived = "position_archived";
    public const string PositionRestored = "position_restored";
    public const string PlatformManagerInviteEmail = "platform_manager_invite_email";
    public const string EmployeeOnboardingInviteEmail = "employee_onboarding_invite_email";
    public const string PositionChangeApprovalRequestEmail = "position_change_approval_request_email";

    // No IOutboxMessageHandler consumer beyond the registered no-op yet - see
    // NoOpEmployeeSecurityOutboxHandler. Producer-side audit trail for now.
    public const string EmployeeSecurityUpdated = "employee_security_updated";
    public const string InvoiceEmail = "invoice_email";
    public const string WorkNotification = "work_notification";
}
