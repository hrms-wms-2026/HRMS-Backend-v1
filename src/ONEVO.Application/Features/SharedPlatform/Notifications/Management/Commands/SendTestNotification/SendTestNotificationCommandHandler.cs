using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Mappers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Rendering;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SendTestNotification;

public sealed class SendTestNotificationCommandHandler
    : IRequestHandler<SendTestNotificationCommand, Result<NotificationTestSendResponse>>
{
    public const int MaxTestSendsPerHour = 10;

    private readonly INotificationTemplateStore _templates;
    private readonly INotificationTemplateRenderer _renderer;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly IPlatformUserRepository _platformUsers;
    private readonly ITransactionalEmailSender _email;
    private readonly ICacheService _cache;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<SendTestNotificationCommandHandler> _logger;

    public SendTestNotificationCommandHandler(
        INotificationTemplateStore templates, INotificationTemplateRenderer renderer,
        ICurrentPlatformUserContext currentUser, IPlatformUserRepository platformUsers,
        ITransactionalEmailSender email, ICacheService cache, IDateTimeProvider clock,
        ILogger<SendTestNotificationCommandHandler> logger)
    {
        _templates = templates;
        _renderer = renderer;
        _currentUser = currentUser;
        _platformUsers = platformUsers;
        _email = email;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<NotificationTestSendResponse>> Handle(SendTestNotificationCommand request, CancellationToken ct)
    {
        // Platform administrators have an email address but no tenant inbox or linked tray device,
        // so email is the only channel a test send can honestly reach.
        if (!string.Equals(request.Channel, NotificationChannelNames.Email, StringComparison.Ordinal))
            return Result<NotificationTestSendResponse>.Failure(
                "Test sends are available for email only. Use Preview to check in-app and tray copy.");
        if (_currentUser.UserId is not { } userId)
            return Result<NotificationTestSendResponse>.Forbidden();

        var template = await _templates.GetTrackedAsync(request.TemplateId, ct);
        if (template is null || !NotificationEventCatalog.TryGet(template.EventKey, out var definition))
            return Result<NotificationTestSendResponse>.NotFound(NotificationTemplateMessages.TemplateNotFound);

        var hourBucket = _clock.UtcNow.ToString("yyyyMMddHH");
        var rateKey = $"platform:notification-test-send:{userId}:{hourBucket}";
        var sent = await _cache.GetAsync<int>(rateKey, ct);
        if (sent >= MaxTestSendsPerHour)
            return Result<NotificationTestSendResponse>.Failure(
                $"Test send limit reached ({MaxTestSendsPerHour} per hour). Try again later.", 429);

        var user = await _platformUsers.GetByIdAsync(userId, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            return Result<NotificationTestSendResponse>.Failure("Your account has no email address to send a test to.");

        var versions = await _templates.ListVersionsAsync(template.Id, ct);
        var version = versions.FirstOrDefault(v => v.Status == NotificationTemplateVersionStatus.Draft)
            ?? versions.FirstOrDefault(v => v.Id == template.ActiveVersionId);
        if (version is null)
            return Result<NotificationTestSendResponse>.NotFound("The template has no content to send.");

        var rendered = _renderer.Render(version.ToContent(), definition, NotificationTemplateMapper.SampleVariables(definition, request.Variables));
        if (!rendered.IsSuccess)
            return Result<NotificationTestSendResponse>.Failure(string.Join(" ", rendered.Errors), 422, "invalid_notification_template");

        await _cache.SetAsync(rateKey, sent + 1, TimeSpan.FromHours(1), ct);
        var outcome = await _email.SendAsync(new TransactionalEmailRequest(
            TenantId: null, user.Email, "[Test] " + rendered.Content!.EmailSubject,
            rendered.Content.EmailHtml, rendered.Content.EmailText, NotificationTemplateId: template.Id), ct);

        // Identifiers only - never the rendered content or the address.
        _logger.LogInformation(
            "Notification template test send for {TemplateId} version {Version} by platform user {UserId}: accepted={Accepted}",
            template.Id, version.Version, userId, outcome.Success);

        return Result<NotificationTestSendResponse>.Success(new NotificationTestSendResponse(
            NotificationChannelNames.Email, user.Email, outcome.Success, outcome.SafeError));
    }
}
