using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Mappers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Rendering;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.PreviewNotificationTemplate;

public sealed class PreviewNotificationTemplateQueryHandler
    : IRequestHandler<PreviewNotificationTemplateQuery, Result<NotificationTemplatePreviewResponse>>
{
    private readonly INotificationTemplateStore _templates;
    private readonly INotificationTemplateRenderer _renderer;

    public PreviewNotificationTemplateQueryHandler(INotificationTemplateStore templates, INotificationTemplateRenderer renderer)
    {
        _templates = templates;
        _renderer = renderer;
    }

    public async Task<Result<NotificationTemplatePreviewResponse>> Handle(PreviewNotificationTemplateQuery request, CancellationToken ct)
    {
        var template = await _templates.GetTrackedAsync(request.TemplateId, ct);
        if (template is null || !NotificationEventCatalog.TryGet(template.EventKey, out var definition))
            return Result<NotificationTemplatePreviewResponse>.NotFound(NotificationTemplateMessages.TemplateNotFound);

        NotificationTemplateContent? content = request.Content?.ToContent();
        if (content is null)
        {
            var versions = await _templates.ListVersionsAsync(template.Id, ct);
            content = (versions.FirstOrDefault(v => v.Status == NotificationTemplateVersionStatus.Draft)
                ?? versions.FirstOrDefault(v => v.Id == template.ActiveVersionId))?.ToContent();
        }
        if (content is null)
            return Result<NotificationTemplatePreviewResponse>.NotFound("The template has no content to preview.");

        var sample = NotificationTemplateMapper.SampleVariables(definition, request.Variables);
        var result = _renderer.Render(content, definition, sample);
        return Result<NotificationTemplatePreviewResponse>.Success(new NotificationTemplatePreviewResponse(
            result.IsSuccess, result.Errors, result.Content?.ToResponse(), sample));
    }
}
