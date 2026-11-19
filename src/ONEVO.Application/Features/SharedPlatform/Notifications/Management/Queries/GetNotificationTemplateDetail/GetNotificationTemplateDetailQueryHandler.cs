using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Mappers;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.GetNotificationTemplateDetail;

public sealed class GetNotificationTemplateDetailQueryHandler
    : IRequestHandler<GetNotificationTemplateDetailQuery, Result<NotificationTemplateDetailResponse>>
{
    private readonly INotificationTemplateStore _templates;

    public GetNotificationTemplateDetailQueryHandler(INotificationTemplateStore templates) => _templates = templates;

    public async Task<Result<NotificationTemplateDetailResponse>> Handle(GetNotificationTemplateDetailQuery request, CancellationToken ct)
    {
        var template = await _templates.GetTrackedAsync(request.TemplateId, ct);
        if (template is null)
            return Result<NotificationTemplateDetailResponse>.NotFound(NotificationTemplateMessages.TemplateNotFound);

        var versions = await _templates.ListVersionsAsync(template.Id, ct);
        return Result<NotificationTemplateDetailResponse>.Success(template.ToDetail(versions));
    }
}
