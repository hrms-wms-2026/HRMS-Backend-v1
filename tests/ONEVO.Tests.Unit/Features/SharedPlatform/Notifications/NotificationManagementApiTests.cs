using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Api.Controllers.Admin.DevPlatform.NotificationTemplates;
using ONEVO.Api.Controllers.Tenant.SharedPlatform;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.CreateNotificationTemplate;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.PublishNotificationTemplate;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.RestoreNotificationTemplateVersion;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SaveNotificationTemplateDraft;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SendTestNotification;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.PreviewNotificationTemplate;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.ResetNotificationSetting;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.UpdateNotificationSetting;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Queries.GetNotificationSettings;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationManagementApiTests
{
    private const string Event = NotificationEventCatalog.FirstSliceEventKey;
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);

    private readonly NotificationTestFakes.FakeTemplateStore _store = new();
    private readonly NotificationTestFakes.FakeRuleRepository _rules = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<ICurrentPlatformUserContext> _admin = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly NotificationTemplateRenderer _renderer = new();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public NotificationManagementApiTests()
    {
        _admin.SetupGet(a => a.UserId).Returns(Guid.NewGuid());
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private static NotificationTemplateContentRequest Content(string title = "Approval needed", string body = "{{actorName}} needs you") =>
        new(title, body, null, null, null, null, null, null, NotificationActionKeys.OpenRelated);

    private NotificationTemplate SeedPublishedDefault()
    {
        var template = new NotificationTemplate { Id = Guid.NewGuid(), Code = Event, EventKey = Event, Name = "Default" };
        var v1 = NotificationTemplateVersion.CreateDraft(template.Id, 1,
            new NotificationTemplateContent("Approval needed", "{{actorName}} body", null, null, null, null, null, null, "open_related"), null, Now);
        v1.Publish(null, Now);
        template.Activate(v1, Now);
        _store.Templates.Add(template);
        _store.Versions.Add(v1);
        return template;
    }

    private Mock<ICurrentUser> TenantUser(Guid tenantId)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.TenantId).Returns(tenantId);
        user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        return user;
    }

    // ---- Platform: templates ----

    [Fact]
    public async Task Create_ForUnregisteredEvent_IsRejected()
    {
        var handler = new CreateNotificationTemplateCommandHandler(_store, _uow.Object, _admin.Object, _clock.Object);

        var result = await handler.Handle(new CreateNotificationTemplateCommand("invented_trigger", "Mine", Content()), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _store.Templates.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_StartsAsUnpublishedDraft()
    {
        var handler = new CreateNotificationTemplateCommandHandler(_store, _uow.Object, _admin.Object, _clock.Object);

        var result = await handler.Handle(new CreateNotificationTemplateCommand(Event, "Friendly wording", Content()), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be($"{Event}.friendly_wording");
        result.Value.ActiveVersion.Should().BeNull();
        result.Value.Draft!.Version.Should().Be(1);
    }

    [Fact]
    public async Task SaveDraft_WithStaleRevision_Returns409()
    {
        var template = SeedPublishedDefault();
        var handler = new SaveNotificationTemplateDraftCommandHandler(_store, _uow.Object, _admin.Object, _clock.Object);
        (await handler.Handle(new SaveNotificationTemplateDraftCommand(template.Id, Content("First"), null), default)).IsSuccess.Should().BeTrue();
        (await handler.Handle(new SaveNotificationTemplateDraftCommand(template.Id, Content("Second"), 1), default)).IsSuccess.Should().BeTrue();

        var stale = await handler.Handle(new SaveNotificationTemplateDraftCommand(template.Id, Content("Third"), 1), default);

        stale.StatusCode.Should().Be(409);
        _store.Versions.Single(v => v.Status == NotificationTemplateVersionStatus.Draft).InAppTitle.Should().Be("Second");
    }

    [Fact]
    public async Task SaveDraft_DoesNotChangeLiveWording()
    {
        var template = SeedPublishedDefault();
        var handler = new SaveNotificationTemplateDraftCommandHandler(_store, _uow.Object, _admin.Object, _clock.Object);

        await handler.Handle(new SaveNotificationTemplateDraftCommand(template.Id, Content("Draft wording"), null), default);

        template.InAppTitleTemplate.Should().Be("Approval needed");
    }

    [Fact]
    public async Task Publish_InvalidDraft_Returns422AndStaysDraft()
    {
        var template = SeedPublishedDefault();
        await new SaveNotificationTemplateDraftCommandHandler(_store, _uow.Object, _admin.Object, _clock.Object)
            .Handle(new SaveNotificationTemplateDraftCommand(template.Id, Content(body: "Your salary is {{salary}}"), null), default);
        var publish = new PublishNotificationTemplateCommandHandler(_store, _renderer, _uow.Object, _admin.Object, _clock.Object);

        var result = await publish.Handle(new PublishNotificationTemplateCommand(template.Id, 1), default);

        result.StatusCode.Should().Be(422);
        result.Error.Should().Contain("salary");
        _store.Versions.Should().ContainSingle(v => v.Status == NotificationTemplateVersionStatus.Draft);
        template.InAppTitleTemplate.Should().Be("Approval needed");
    }

    [Fact]
    public async Task Publish_ValidDraft_BecomesLiveWording()
    {
        var template = SeedPublishedDefault();
        await new SaveNotificationTemplateDraftCommandHandler(_store, _uow.Object, _admin.Object, _clock.Object)
            .Handle(new SaveNotificationTemplateDraftCommand(template.Id, Content("New wording"), null), default);
        var publish = new PublishNotificationTemplateCommandHandler(_store, _renderer, _uow.Object, _admin.Object, _clock.Object);

        var result = await publish.Handle(new PublishNotificationTemplateCommand(template.Id, 1), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActiveVersion!.Version.Should().Be(2);
        result.Value.Draft.Should().BeNull();
        template.InAppTitleTemplate.Should().Be("New wording");
    }

    [Fact]
    public async Task Publish_StaleRevision_Returns409()
    {
        var template = SeedPublishedDefault();
        await new SaveNotificationTemplateDraftCommandHandler(_store, _uow.Object, _admin.Object, _clock.Object)
            .Handle(new SaveNotificationTemplateDraftCommand(template.Id, Content("New wording"), null), default);
        var publish = new PublishNotificationTemplateCommandHandler(_store, _renderer, _uow.Object, _admin.Object, _clock.Object);

        (await publish.Handle(new PublishNotificationTemplateCommand(template.Id, 7), default)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Restore_CopiesPublishedVersionIntoNewDraft_AndRefusesWhenADraftExists()
    {
        var template = SeedPublishedDefault();
        var v1 = _store.Versions.Single();
        var restore = new RestoreNotificationTemplateVersionCommandHandler(_store, _uow.Object, _admin.Object, _clock.Object);

        var result = await restore.Handle(new RestoreNotificationTemplateVersionCommand(template.Id, v1.Id), default);
        var again = await restore.Handle(new RestoreNotificationTemplateVersionCommand(template.Id, v1.Id), default);

        result.Value!.Draft!.Version.Should().Be(2);
        result.Value.Draft.InAppTitle.Should().Be(v1.InAppTitle);
        again.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Preview_RendersUnsavedContentWithSampleVariables()
    {
        var template = SeedPublishedDefault();
        var preview = new PreviewNotificationTemplateQueryHandler(_store, _renderer);

        var result = await preview.Handle(new PreviewNotificationTemplateQuery(
            template.Id, Content(body: "{{actorName}} asks"), new Dictionary<string, string> { ["actorName"] = "Priya" }), default);

        result.Value!.IsValid.Should().BeTrue();
        result.Value.Rendered!.InAppBody.Should().Be("Priya asks");
    }

    [Fact]
    public async Task TestSend_OnlyEmailsTheRequestingAdmin_AndIsRateLimited()
    {
        var template = SeedPublishedDefault();
        var adminId = _admin.Object.UserId!.Value;
        var users = new Mock<IPlatformUserRepository>();
        users.Setup(u => u.GetByIdAsync(adminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities.PlatformUser { Id = adminId, Email = "admin@onevo.test" });
        var email = new Mock<ITransactionalEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<TransactionalEmailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Sent("test", "m1", Now));
        var cache = new NotificationTestFakes.FakeCache();
        var handler = new SendTestNotificationCommandHandler(_store, _renderer, _admin.Object, users.Object, email.Object,
            cache, _clock.Object, NullLogger<SendTestNotificationCommandHandler>.Instance);

        for (var i = 0; i < SendTestNotificationCommandHandler.MaxTestSendsPerHour; i++)
            (await handler.Handle(new SendTestNotificationCommand(template.Id, "email", null), default)).IsSuccess.Should().BeTrue();
        var limited = await handler.Handle(new SendTestNotificationCommand(template.Id, "email", null), default);
        var tray = await handler.Handle(new SendTestNotificationCommand(template.Id, "tray", null), default);

        limited.StatusCode.Should().Be(429);
        tray.StatusCode.Should().Be(400);
        email.Verify(e => e.SendAsync(It.Is<TransactionalEmailRequest>(r => r.RecipientEmail == "admin@onevo.test" && r.TenantId == null),
            It.IsAny<CancellationToken>()), Times.Exactly(SendTestNotificationCommandHandler.MaxTestSendsPerHour));
    }

    // ---- Tenant: settings ----

    private UpdateNotificationSettingCommandHandler UpdateHandler(Guid tenantId)
        => new(TenantUser(tenantId).Object, _rules, _store, _audit.Object, _uow.Object, _clock.Object);

    [Fact]
    public async Task Settings_DefaultsToCatalogChannels()
    {
        SeedPublishedDefault();
        var handler = new GetNotificationSettingsQueryHandler(TenantUser(_tenantA).Object, _rules, _store);

        var result = await handler.Handle(new GetNotificationSettingsQuery(), default);

        var setting = result.Value!.Single(s => s.EventKey == Event);
        setting.EnabledChannels.Should().Equal(NotificationChannelNames.InApp);
        setting.SupportedChannels.Should().Equal(NotificationChannelNames.InApp, NotificationChannelNames.Email, NotificationChannelNames.Tray);
        setting.IsOverridden.Should().BeFalse();
        setting.Templates.Should().ContainSingle(t => t.IsProductDefault);
    }

    [Fact]
    public async Task Update_UnsupportedChannel_IsRejected()
    {
        var monitoringEvent = NotificationEventCatalog.MonitoringEventKey(ONEVO.Domain.Features.Monitoring.Notifications.Entities.NotificationType.BreakReminder);

        var result = await UpdateHandler(_tenantA).Handle(new UpdateNotificationSettingCommand(monitoringEvent, ["tray", "email"], null, null, null), default);

        result.StatusCode.Should().Be(400);
        _rules.Rules.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_UnknownRecipientRule_IsRejected()
    {
        var result = await UpdateHandler(_tenantA).Handle(new UpdateNotificationSettingCommand(Event, ["email"], null, "everyone_in_company", null), default);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Update_TemplateFromAnotherEvent_IsRejected()
    {
        var other = new NotificationTemplate { Id = Guid.NewGuid(), Code = "work_sprint_completed", EventKey = "work_sprint_completed", ActiveVersionId = Guid.NewGuid() };
        _store.Templates.Add(other);

        var result = await UpdateHandler(_tenantA).Handle(new UpdateNotificationSettingCommand(Event, ["email"], other.Id, null, null), default);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Update_UnpublishedTemplate_IsRejected()
    {
        var draftOnly = new NotificationTemplate { Id = Guid.NewGuid(), Code = Event + ".x", EventKey = Event };
        _store.Templates.Add(draftOnly);

        var result = await UpdateHandler(_tenantA).Handle(new UpdateNotificationSettingCommand(Event, ["email"], draftOnly.Id, null, null), default);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Update_SavesIndependentChannels_AndWritesAudit()
    {
        var result = await UpdateHandler(_tenantA).Handle(new UpdateNotificationSettingCommand(Event, ["email", "tray"], null, null, null), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EnabledChannels.Should().Equal(NotificationChannelNames.Email, NotificationChannelNames.Tray);
        _rules.Rules.Single().EnabledChannels.Should().Be(NotificationChannels.Email | NotificationChannels.Tray);
        _audit.Verify(a => a.AddAsync(It.Is<AuditLog>(l => l.TenantId == _tenantA && l.Action == "notification_settings.updated"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_StaleRevision_Returns409()
    {
        var handler = UpdateHandler(_tenantA);
        await handler.Handle(new UpdateNotificationSettingCommand(Event, ["email"], null, null, null), default);
        await handler.Handle(new UpdateNotificationSettingCommand(Event, ["in_app"], null, null, 1), default);

        var stale = await handler.Handle(new UpdateNotificationSettingCommand(Event, ["tray"], null, null, 1), default);
        var createdMeanwhile = await handler.Handle(new UpdateNotificationSettingCommand(Event, ["tray"], null, null, null), default);

        stale.StatusCode.Should().Be(409);
        createdMeanwhile.StatusCode.Should().Be(409);
        _rules.Rules.Single().EnabledChannels.Should().Be(NotificationChannels.InApp);
    }

    [Fact]
    public async Task Update_OnlyTouchesTheCallersTenant()
    {
        await UpdateHandler(_tenantB).Handle(new UpdateNotificationSettingCommand(Event, ["tray"], null, null, null), default);

        await UpdateHandler(_tenantA).Handle(new UpdateNotificationSettingCommand(Event, ["email"], null, null, null), default);

        _rules.Rules.Single(r => r.TenantId == _tenantB).EnabledChannels.Should().Be(NotificationChannels.Tray);
        _rules.Rules.Single(r => r.TenantId == _tenantA).EnabledChannels.Should().Be(NotificationChannels.Email);
    }

    [Fact]
    public async Task Reset_RemovesOverride_AndReturnsDefaults()
    {
        await UpdateHandler(_tenantA).Handle(new UpdateNotificationSettingCommand(Event, ["email"], null, null, null), default);
        var reset = new ResetNotificationSettingCommandHandler(TenantUser(_tenantA).Object, _rules, _store, _audit.Object, _uow.Object, _clock.Object);

        var result = await reset.Handle(new ResetNotificationSettingCommand(Event), default);

        _rules.Rules.Should().BeEmpty();
        result.Value!.IsOverridden.Should().BeFalse();
        result.Value.EnabledChannels.Should().Equal(NotificationChannelNames.InApp);
    }

    [Fact]
    public void RequiredChannels_AreAlwaysOn_EvenIfAStoredRuleDisablesThem()
    {
        var definition = new NotificationEventDefinition("security_event", "auth", "Security",
            NotificationChannels.InApp | NotificationChannels.Email, NotificationChannels.Email, NotificationChannels.Email,
            [], [NotificationRecipientRules.Default], [NotificationActionKeys.None]);

        var effective = EffectiveNotificationRule.Resolve(definition, new TenantNotificationRule { EnabledChannels = NotificationChannels.InApp | NotificationChannels.Tray });

        effective.Channels.Should().Be(NotificationChannels.InApp | NotificationChannels.Email);
    }

    // ---- Permission boundaries ----

    [Fact]
    public void PlatformEndpoints_RequirePlatformPermissions_AndMutationsRequireManage()
    {
        foreach (var controller in new[] { typeof(AdminNotificationTemplatesController), typeof(AdminNotificationEventsController) })
        {
            foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var permission = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
                permission.Should().NotBeNull($"{controller.Name}.{method.Name}");
                var mutates = method.GetCustomAttributes().Any(a => a is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute)
                    && method.Name != "Preview";
                if (mutates)
                    permission!.Permission.Should().Be("platform.templates.manage", method.Name);
            }
        }
    }

    [Fact]
    public void TenantSettingsController_RequiresNotificationsManage()
    {
        var attribute = typeof(NotificationSettingsController).GetCustomAttribute<RequirePermissionAttribute>();

        attribute.Should().NotBeNull();
        typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(attribute).Should().Be("notifications:manage");
    }
}
