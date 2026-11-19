using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Notifications;

/// <summary>
/// Sends the captured email. Delivery is at-least-once: if the provider accepts the message but the status
/// save then fails (or the provider times out after accepting), the retry sends it again. The send carries an
/// idempotency key from the delivery's id - the same on every retry, with the same captured body - so a
/// provider that honours keys (Resend) drops the repeat; one that does not (SendGrid) may deliver it twice.
/// Internal duplicates are prevented by the delivery identity.
/// </summary>
public sealed class NotificationEmailDeliveryHandler : INotificationChannelDeliveryHandler
{
    /// <summary>Matches the outbox processor's attempt cap.</summary>
    public const int MaxAttempts = 8;

    private readonly IUserRepository _users;
    private readonly ITransactionalEmailSender _email;
    private readonly IDateTimeProvider _clock;

    public NotificationEmailDeliveryHandler(IUserRepository users, ITransactionalEmailSender email, IDateTimeProvider clock)
    {
        _users = users;
        _email = email;
        _clock = clock;
    }

    public NotificationChannels Channel => NotificationChannels.Email;

    public async Task DeliverAsync(NotificationDelivery delivery, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var user = await _users.GetByIdAsync(delivery.RecipientUserId, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            delivery.Status = NotificationDeliveryStatus.Skipped;
            delivery.StatusReason = "no_email_address";
            delivery.UpdatedAt = now;
            delivery.CompletedAt = now;
            return;
        }

        delivery.AttemptCount++;
        var result = await _email.SendAsync(new TransactionalEmailRequest(
            delivery.TenantId, user.Email, delivery.EmailSubject ?? delivery.Title,
            delivery.EmailHtml ?? string.Empty, delivery.EmailText, IdempotencyKey: IdempotencyKey(delivery)), ct);
        delivery.UpdatedAt = _clock.UtcNow;

        if (result.Success)
        {
            delivery.Status = NotificationDeliveryStatus.ProviderAccepted;
            delivery.StatusReason = null;
            delivery.CompletedAt = delivery.UpdatedAt;
            return;
        }

        delivery.StatusReason = Truncate(result.SafeError ?? "email_send_failed", 500);
        if (delivery.AttemptCount >= MaxAttempts)
        {
            delivery.Status = NotificationDeliveryStatus.Failed;
            delivery.CompletedAt = delivery.UpdatedAt;
            return;
        }

        throw new NotificationDeliveryRetryException($"Email delivery {delivery.Id} failed; will retry.");
    }

    /// <summary>One key per delivery: stable across retries, unlike a key counted per outbox attempt.</summary>
    public static string IdempotencyKey(NotificationDelivery delivery) => $"notification-delivery/{delivery.Id}";

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
