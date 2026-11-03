using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Mappers;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.ListNotificationTemplates;

public sealed class ListNotificationTemplatesQueryHandler
    : IRequestHandler<ListNotificationTemplatesQuery, Result<IReadOnlyList<NotificationTemplateSummaryResponse>>>
{
    private readonly INotificationTemplateStore _templates;

    public ListNotificationTemplatesQueryHandler(INotificationTemplateStore templates) => _templates = templates;

    public async Task<Result<IReadOnlyList<NotificationTemplateSummaryResponse>>> Handle(ListNotificationTemplatesQuery request, CancellationToken ct)
    {
        var templates = await _templates.ListAsync(string.IsNullOrWhiteSpace(request.EventKey) ? null : request.EventKey, ct);
        var search = request.Search?.Trim();

        var result = new List<NotificationTemplateSummaryResponse>();
        foreach (var t in templates.Where(t => string.IsNullOrEmpty(search)
                     || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                     || t.EventKey.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            var versions = await _templates.ListVersionsAsync(t.Id, ct);
            result.Add(new NotificationTemplateSummaryResponse(
                t.Id, t.Code, t.EventKey, t.Name, t.Locale, t.IsProductDefault(),
                versions.FirstOrDefault(v => v.Id == t.ActiveVersionId)?.Version,
                versions.Any(v => v.Status == NotificationTemplateVersionStatus.Draft),
                t.UpdatedAt ?? t.CreatedAt));
        }

        return Result<IReadOnlyList<NotificationTemplateSummaryResponse>>.Success(
            result.OrderBy(t => t.EventKey).ThenByDescending(t => t.IsProductDefault).ThenBy(t => t.Name).ToList());
    }
}
