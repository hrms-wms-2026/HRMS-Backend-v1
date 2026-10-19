using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Mappers;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.RestoreNotificationTemplateVersion;

public sealed class RestoreNotificationTemplateVersionCommandHandler
    : IRequestHandler<RestoreNotificationTemplateVersionCommand, Result<NotificationTemplateDetailResponse>>
{
    private readonly INotificationTemplateStore _templates;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly IDateTimeProvider _clock;

    public RestoreNotificationTemplateVersionCommandHandler(
        INotificationTemplateStore templates, IUnitOfWork unitOfWork,
        ICurrentPlatformUserContext currentUser, IDateTimeProvider clock)
    {
        _templates = templates;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<NotificationTemplateDetailResponse>> Handle(RestoreNotificationTemplateVersionCommand request, CancellationToken ct)
    {
        var template = await _templates.GetTrackedAsync(request.TemplateId, ct);
        if (template is null)
            return Result<NotificationTemplateDetailResponse>.NotFound(NotificationTemplateMessages.TemplateNotFound);

        var source = await _templates.GetVersionAsync(request.VersionId, ct);
        if (source is null || source.TemplateId != template.Id)
            return Result<NotificationTemplateDetailResponse>.NotFound("Template version not found.");
        if (source.Status != NotificationTemplateVersionStatus.Published)
            return Result<NotificationTemplateDetailResponse>.Failure("Only published versions can be restored.");
        if (await _templates.GetTrackedDraftAsync(template.Id, ct) is not null)
            return Result<NotificationTemplateDetailResponse>.Conflict(NotificationTemplateMessages.DraftExists);

        var next = await _templates.GetLatestVersionNumberAsync(template.Id, ct) + 1;
        await _templates.AddVersionAsync(
            NotificationTemplateVersion.CreateDraft(template.Id, next, source.ToContent(), _currentUser.UserId, _clock.UtcNow), ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var versions = await _templates.ListVersionsAsync(template.Id, ct);
        return Result<NotificationTemplateDetailResponse>.Success(template.ToDetail(versions));
    }
}
