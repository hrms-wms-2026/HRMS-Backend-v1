using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Infrastructure.Services.SharedPlatform.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

/// <summary>
/// Plan Task 8 regression cases, one per inventoried event: after cut-over a producer's existing call still
/// reaches the same recipient with the same template code, copy and click target, through the real orchestrator.
/// </summary>
public class NotificationEventMigrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    /// <summary>Seeded in the catalog but no code raises them; nothing to migrate.</summary>
    private static readonly string[] ProducerlessEvents =
    [
        "work_task_creation_request_created", "work_task_creation_request_decided", "work_task_edit_request_decided",
        "work_task_status_change_request_created", "work_task_status_change_request_decided",
        "work_allocation_extend_request_created", "work_allocation_extend_request_decided",
        "work_objective_edit_request_created", "work_objective_edit_request_decided"
    ];

    /// <summary>Their producers still send their own email (employee-record address, ICS attachments), so the
    /// catalog must not offer a second, orchestrator email.</summary>
    private static readonly string[] ProducerSendsOwnEmail =
    [
        "leave_request_approved", "leave_request_rejected",
        "leave_request_cancelled_by_employee", "leave_request_cancelled_by_hr", "leave_request_partially_cancelled",
        "calendar_event_participant_added", "calendar_event_updated", "calendar_event_cancelled",
        "calendar_event_participant_removed", "calendar_event_meeting_link_removed"
    ];

    public static TheoryData<string> MigratedEvents()
    {
        var data = new TheoryData<string>();
        foreach (var code in NotificationTemplateSeeder.DefaultTemplates().Select(t => t.Code).Except(ProducerlessEvents))
            data.Add(code);
        return data;
    }

    [Fact]
    public void EveryProducerBackedSharedEvent_IsOwnedByTheOrchestrator_AndOthersAreNot()
    {
        foreach (var template in NotificationTemplateSeeder.DefaultTemplates())
            NotificationEventCatalog.Get(template.Code).OrchestratorOwned.Should().Be(!ProducerlessEvents.Contains(template.Code), template.Code);
    }

    [Fact]
    public void MonitoringTrayAlerts_StayOnTheirLegacyTransport()
    {
        NotificationEventCatalog.All.Where(e => e.Key.StartsWith("monitoring_", StringComparison.Ordinal))
            .Should().OnlyContain(e => !e.OrchestratorOwned && e.SupportedChannels == NotificationChannels.Tray);
    }

    [Fact]
    public void EventsWhoseProducerSendsItsOwnEmail_DoNotOfferEmail()
    {
        foreach (var key in ProducerSendsOwnEmail)
        {
            var e = NotificationEventCatalog.Get(key);
            e.OrchestratorOwned.Should().BeTrue(key);
            e.SupportedChannels.Should().Be(NotificationChannels.InApp | NotificationChannels.Tray, key);
        }
    }

    [Fact]
    public void OtherMigratedEvents_OfferAllThreeChannels_WithInAppDefault()
    {
        foreach (var e in NotificationEventCatalog.All.Where(e => e.OrchestratorOwned && !ProducerSendsOwnEmail.Contains(e.Key)))
        {
            e.SupportedChannels.Should().Be(NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Tray, e.Key);
            e.DefaultChannels.Should().Be(NotificationChannels.InApp, e.Key);
        }
    }

    [Fact]
    public void EverySeededDefaultTemplate_ValidatesAgainstItsEvent()
    {
        var renderer = new NotificationTemplateRenderer();
        foreach (var t in NotificationTemplateSeeder.DefaultTemplates())
        {
            var content = new NotificationTemplateContent(t.InAppTitleTemplate, t.InAppBodyTemplate, null, null, null, null, null, null, NotificationActionKeys.OpenRelated);
            renderer.Validate(content, NotificationEventCatalog.Get(t.Code)).Should().BeEmpty(t.Code);
        }
    }

    [Theory]
    [MemberData(nameof(MigratedEvents))]
    public async Task ProducerCall_StillDeliversTheSameInAppNotification(string code)
    {
        var seeded = NotificationTemplateSeeder.DefaultTemplates().Single(t => t.Code == code);
        var store = new NotificationTestFakes.FakeTemplateStore();
        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(), Code = code, EventKey = code, Name = code,
            InAppTitleTemplate = seeded.InAppTitleTemplate, InAppBodyTemplate = seeded.InAppBodyTemplate
        };
        var version = NotificationTemplateVersion.CreateDraft(template.Id, 1,
            new NotificationTemplateContent(seeded.InAppTitleTemplate, seeded.InAppBodyTemplate, null, null, null, null, null, null, NotificationActionKeys.OpenRelated),
            null, Now);
        version.Publish(null, Now);
        template.Activate(version, Now);
        store.Templates.Add(template);
        store.Versions.Add(version);

        var rows = new List<Notification>();
        var inbox = new Mock<INotificationRepository>();
        inbox.Setup(i => i.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, _) => rows.Add(n)).Returns(Task.CompletedTask);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        var orchestrator = new NotificationOrchestrator(
            new NotificationTestFakes.FakeRuleRepository(), store, new NotificationTestFakes.FakeDeliveryRepository(),
            new NotificationTemplateRenderer(), new Mock<IOutboxWriter>().Object, clock.Object,
            NullLogger<NotificationOrchestrator>.Instance, inbox.Object);
        var dispatcher = new NotificationDispatcher(inbox.Object, orchestrator);

        var variables = Placeholder.Matches(seeded.InAppTitleTemplate + seeded.InAppBodyTemplate)
            .Select(m => m.Groups[1].Value).Distinct().ToDictionary(v => v, v => $"<{v}>");
        var tenant = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var related = Guid.NewGuid();

        await dispatcher.SendTemplatedAsync(tenant, recipient, code, variables, "inbox:test:item", related);

        var row = rows.Should().ContainSingle().Subject;
        row.TenantId.Should().Be(tenant);
        row.RecipientUserId.Should().Be(recipient);
        row.TemplateCode.Should().Be(code);
        row.RelatedEntityType.Should().Be("inbox:test:item");
        row.RelatedEntityId.Should().Be(related);
        row.Title.Should().Be(Legacy(seeded.InAppTitleTemplate, variables));
        row.Body.Should().Be(Legacy(seeded.InAppBodyTemplate, variables));
    }

    /// <summary>What the legacy dispatcher produced: plain placeholder replacement.</summary>
    private static string Legacy(string template, IReadOnlyDictionary<string, string> variables)
        => variables.Aggregate(template, (text, kv) => text.Replace("{{" + kv.Key + "}}", kv.Value));
}
