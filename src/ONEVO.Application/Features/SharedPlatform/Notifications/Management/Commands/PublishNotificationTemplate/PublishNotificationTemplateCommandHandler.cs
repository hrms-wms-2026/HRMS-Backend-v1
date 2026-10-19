using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Mappers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Rendering;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.PublishNotificationTemplate;

public sealed class PublishNotificationTemplateCommandHandler
    : IRequestHandler<PublishNotificationTemplateCommand, Result<NotificationTemplateDetailResponse>>
{
    private readonly INotificationTemplateStore _templates;
    private readonly INotificationTemplateRenderer _renderer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly IDateTimeProvider _clock;

    public PublishNotificationTemplateCommandHandler(
        INotificationTemplateStore templates, INotificationTemplateRenderer renderer, IUnitOfWork unitOfWork,
        ICurrentPlatformUserContext currentUser, IDateTimeProvider clock)
    {
        _templates = templates;
        _renderer = renderer;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<NotificationTemplateDetailResponse>> Handle(PublishNotificationTemplateCommand request, CancellationToken ct)
    {
        var template = await _templates.GetTrackedAsync(request.TemplateId, ct);
        if (template is null || !NotificationEventCatalog.TryGet(template.EventKey, out var definition))
            return Result<NotificationTemplateDetailResponse>.NotFound(NotificationTemplateMessages.TemplateNotFound);

        var draft = await _templates.GetTrackedDraftAsync(template.Id, ct);
        if (draft is null)
            return Result<NotificationTemplateDetailResponse>.Conflict(NotificationTemplateMessages.NoDraft);
        if (draft.Revision != request.ExpectedRevision)
            return Result<NotificationTemplateDetailResponse>.Conflict(NotificationTemplateMessages.StaleDraft);

        var errors = _renderer.Validate(draft.ToContent(), definition);
        if (errors.Count > 0)
            return Result<NotificationTemplateDetailResponse>.Failure(string.Join(" ", errors), 422, "invalid_notification_template");

        var now = _clock.UtcNow;
        draft.Publish(_currentUser.UserId, now);
        template.Activate(draft, now);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<NotificationTemplateDetailResponse>.Conflict(NotificationTemplateMessages.StaleDraft);
        }

        var versions = await _templates.ListVersionsAsync(template.Id, ct);
        return Result<NotificationTemplateDetailResponse>.Success(template.ToDetail(versions));
    }
}
