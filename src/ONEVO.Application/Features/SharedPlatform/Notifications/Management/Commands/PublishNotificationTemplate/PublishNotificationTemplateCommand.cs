using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.PublishNotificationTemplate;

/// <summary>Validates and publishes the draft, making it the live wording for new sends.</summary>
public sealed record PublishNotificationTemplateCommand(Guid TemplateId, int ExpectedRevision)
    : IRequest<Result<NotificationTemplateDetailResponse>>, IAuditedCommand<Result<NotificationTemplateDetailResponse>>
{
    public AuditEntry DescribeAudit(Result<NotificationTemplateDetailResponse>? response)
        => AuditEntry.For("notification_template.published", "notification_template", TemplateId, null, new { ExpectedRevision });
}
