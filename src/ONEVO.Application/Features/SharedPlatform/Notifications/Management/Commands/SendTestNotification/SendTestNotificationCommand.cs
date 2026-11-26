using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SendTestNotification;

/// <summary>Sends the draft (or active version) with sample variables to the requesting admin only.
/// Never creates business deliveries.</summary>
public sealed record SendTestNotificationCommand(Guid TemplateId, string Channel, IReadOnlyDictionary<string, string>? Variables)
    : IRequest<Result<NotificationTestSendResponse>>, IAuditedCommand<Result<NotificationTestSendResponse>>
{
    public AuditEntry DescribeAudit(Result<NotificationTestSendResponse>? response)
        => AuditEntry.For("notification_template.test_sent", "notification_template", TemplateId, null, new { Channel, Recipient = response?.Value?.Recipient, Accepted = response?.Value?.Accepted });
}
