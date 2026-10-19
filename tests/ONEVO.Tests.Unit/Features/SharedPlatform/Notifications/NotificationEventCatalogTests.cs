using System.Text.RegularExpressions;
using FluentAssertions;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationEventCatalogTests
{
    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    [Fact]
    public void EventKeys_AreUnique()
    {
        NotificationEventCatalog.All.Select(e => e.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EverySeededTemplateCode_IsRegistered()
    {
        var seeded = NotificationTemplateSeeder.DefaultTemplates().Select(t => t.Code);

        seeded.Should().AllSatisfy(code => NotificationEventCatalog.IsRegistered(code).Should().BeTrue(code));
    }

    [Fact]
    public void EveryMonitoringNotificationType_IsRegistered()
    {
        foreach (var type in Enum.GetValues<NotificationType>())
            NotificationEventCatalog.IsRegistered(NotificationEventCatalog.MonitoringEventKey(type)).Should().BeTrue(type.ToString());
    }

    [Fact]
    public void DefaultAndRequiredChannels_AreSubsetsOfSupportedChannels()
    {
        foreach (var e in NotificationEventCatalog.All)
        {
            (e.DefaultChannels & ~e.SupportedChannels).Should().Be(NotificationChannels.None, e.Key);
            (e.RequiredChannels & ~e.SupportedChannels).Should().Be(NotificationChannels.None, e.Key);
            e.SupportedChannels.Should().NotBe(NotificationChannels.None, e.Key);
        }
    }

    [Fact]
    public void RecipientRulesAndActionKeys_AreExplicitlyRegistered()
    {
        foreach (var e in NotificationEventCatalog.All)
        {
            e.AllowedRecipientRules.Should().NotBeEmpty(e.Key);
            e.AllowedActionKeys.Should().NotBeEmpty(e.Key);
            e.AllowedActionKeys.Should().BeSubsetOf(NotificationActionKeys.All, e.Key);
        }
    }

    [Fact]
    public void SeededTemplatePlaceholders_AreAllowedVariables()
    {
        foreach (var template in NotificationTemplateSeeder.DefaultTemplates())
        {
            var definition = NotificationEventCatalog.Get(template.Code);
            var used = Placeholder.Matches(template.InAppTitleTemplate + template.InAppBodyTemplate)
                .Select(m => m.Groups[1].Value).Distinct();
            used.Should().BeSubsetOf(definition.AllowedVariables.Select(v => v.Key), template.Code);
        }
    }

    [Fact]
    public void UnknownEvent_FailsValidation()
    {
        NotificationEventCatalog.IsRegistered("not_a_real_event").Should().BeFalse();
        NotificationEventCatalog.TryGet("not_a_real_event", out _).Should().BeFalse();
        var act = () => NotificationEventCatalog.Get("not_a_real_event");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void FirstSliceEvent_SupportsAllThreeChannels_WithExistingInAppDefault()
    {
        var e = NotificationEventCatalog.Get(NotificationEventCatalog.FirstSliceEventKey);

        e.Module.Should().Be("work_management");
        e.OrchestratorOwned.Should().BeTrue();
        e.SupportedChannels.Should().Be(NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Tray);
        e.DefaultChannels.Should().Be(NotificationChannels.InApp);
    }

    [Fact]
    public void LegacyOwnedEvents_OnlyAdvertiseTheChannelTheirProducerActuallyDelivers()
    {
        foreach (var e in NotificationEventCatalog.All.Where(e => !e.OrchestratorOwned))
            e.SupportedChannels.Should().Be(e.Key.StartsWith("monitoring_", StringComparison.Ordinal) ? NotificationChannels.Tray : NotificationChannels.InApp, e.Key);
    }
}
