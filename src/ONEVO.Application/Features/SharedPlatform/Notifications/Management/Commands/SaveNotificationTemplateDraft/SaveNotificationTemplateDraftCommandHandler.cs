using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Mappers;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SaveNotificationTemplateDraft;

public sealed class SaveNotificationTemplateDraftCommandHandler
    : IRequestHandler<SaveNotificationTemplateDraftCommand, Result<NotificationTemplateDetailResponse>>
{
    private readonly INotificationTemplateStore _templates;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly IDateTimeProvider _clock;

    public SaveNotificationTemplateDraftCommandHandler(
        INotificationTemplateStore templates, IUnitOfWork unitOfWork,
        ICurrentPlatformUserContext currentUser, IDateTimeProvider clock)
    {
        _templates = templates;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<NotificationTemplateDetailResponse>> Handle(SaveNotificationTemplateDraftCommand request, CancellationToken ct)
    {
        var template = await _templates.GetTrackedAsync(request.TemplateId, ct);
        if (template is null)
            return Result<NotificationTemplateDetailResponse>.NotFound(NotificationTemplateMessages.TemplateNotFound);

        var content = request.Content.ToContent();
        if (NotificationTemplateMessages.CheckLengths(content) is { } lengthError)
            return Result<NotificationTemplateDetailResponse>.Failure(lengthError);

        var now = _clock.UtcNow;
        var draft = await _templates.GetTrackedDraftAsync(template.Id, ct);
        if (draft is null)
        {
            var next = await _templates.GetLatestVersionNumberAsync(template.Id, ct) + 1;
            await _templates.AddVersionAsync(NotificationTemplateVersion.CreateDraft(template.Id, next, content, _currentUser.UserId, now), ct);
        }
        else if (draft.UpdateDraft(content, request.ExpectedRevision ?? -1, _currentUser.UserId, now) != DraftUpdateOutcome.Updated)
        {
            return Result<NotificationTemplateDetailResponse>.Conflict(NotificationTemplateMessages.StaleDraft);
        }

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
