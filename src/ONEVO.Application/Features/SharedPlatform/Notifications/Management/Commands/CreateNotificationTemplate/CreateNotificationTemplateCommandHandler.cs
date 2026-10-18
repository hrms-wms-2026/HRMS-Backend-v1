using System.Text;
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
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.CreateNotificationTemplate;

public sealed class CreateNotificationTemplateCommandHandler
    : IRequestHandler<CreateNotificationTemplateCommand, Result<NotificationTemplateDetailResponse>>
{
    private readonly INotificationTemplateStore _templates;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly IDateTimeProvider _clock;

    public CreateNotificationTemplateCommandHandler(
        INotificationTemplateStore templates, IUnitOfWork unitOfWork,
        ICurrentPlatformUserContext currentUser, IDateTimeProvider clock)
    {
        _templates = templates;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<NotificationTemplateDetailResponse>> Handle(CreateNotificationTemplateCommand request, CancellationToken ct)
    {
        if (!NotificationEventCatalog.TryGet(request.EventKey ?? string.Empty, out var definition))
            return Result<NotificationTemplateDetailResponse>.Failure(
                "Templates can only be created for a registered notification event.", 400, "unknown_notification_event");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 150)
            return Result<NotificationTemplateDetailResponse>.Failure("Template name is required (at most 150 characters).");

        var content = request.Content.ToContent();
        if (NotificationTemplateMessages.CheckLengths(content) is { } lengthError)
            return Result<NotificationTemplateDetailResponse>.Failure(lengthError);

        var code = $"{definition.Key}.{Slug(name)}";
        if (code.Length > 100)
            code = code[..100];
        if (await _templates.CodeExistsAsync(code, ct))
            return Result<NotificationTemplateDetailResponse>.Conflict("A template with this name already exists for the event.");

        var now = _clock.UtcNow;
        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(), Code = code, EventKey = definition.Key, Name = name, Locale = "en",
            InAppTitleTemplate = content.InAppTitle, InAppBodyTemplate = content.InAppBody, CreatedAt = now
        };
        var draft = NotificationTemplateVersion.CreateDraft(template.Id, 1, content, _currentUser.UserId, now);

        await _templates.AddTemplateAsync(template, ct);
        await _templates.AddVersionAsync(draft, ct);
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<NotificationTemplateDetailResponse>.Conflict("A template with this name already exists for the event.");
        }

        return Result<NotificationTemplateDetailResponse>.Success(template.ToDetail([draft]));
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        var slug = string.Join('_', sb.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "template" : slug;
    }
}
