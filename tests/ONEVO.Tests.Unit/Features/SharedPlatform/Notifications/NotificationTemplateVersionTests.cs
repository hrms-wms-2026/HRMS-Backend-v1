using FluentAssertions;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationTemplateVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();

    private static NotificationTemplateContent Content(string title = "Approval needed") => new(
        InAppTitle: title, InAppBody: "{{actorName}} needs you",
        EmailSubject: null, EmailHtml: null, EmailCss: null, EmailText: null,
        TrayTitle: null, TrayBody: null, ActionKey: "open_related");

    [Fact]
    public void NewDraft_StartsAtRevisionOne()
    {
        var draft = NotificationTemplateVersion.CreateDraft(Guid.NewGuid(), 2, Content(), Admin, Now);

        draft.Status.Should().Be(NotificationTemplateVersionStatus.Draft);
        draft.Version.Should().Be(2);
        draft.Revision.Should().Be(1);
    }

    [Fact]
    public void UpdateDraft_WithCurrentRevision_AppliesContentAndBumpsRevision()
    {
        var draft = NotificationTemplateVersion.CreateDraft(Guid.NewGuid(), 1, Content(), Admin, Now);

        var outcome = draft.UpdateDraft(Content("Changed"), expectedRevision: 1, Admin, Now);

        outcome.Should().Be(DraftUpdateOutcome.Updated);
        draft.InAppTitle.Should().Be("Changed");
        draft.Revision.Should().Be(2);
    }

    [Fact]
    public void UpdateDraft_WithStaleRevision_IsRejectedWithoutChanges()
    {
        var draft = NotificationTemplateVersion.CreateDraft(Guid.NewGuid(), 1, Content(), Admin, Now);
        draft.UpdateDraft(Content("First editor"), 1, Admin, Now);

        var outcome = draft.UpdateDraft(Content("Second editor"), expectedRevision: 1, Admin, Now);

        outcome.Should().Be(DraftUpdateOutcome.StaleRevision);
        draft.InAppTitle.Should().Be("First editor");
    }

    [Fact]
    public void PublishedVersion_IsImmutable()
    {
        var version = NotificationTemplateVersion.CreateDraft(Guid.NewGuid(), 1, Content(), Admin, Now);
        version.Publish(Admin, Now).Should().BeTrue();

        var outcome = version.UpdateDraft(Content("Sneaky edit"), version.Revision, Admin, Now);

        outcome.Should().Be(DraftUpdateOutcome.NotDraft);
        version.InAppTitle.Should().Be("Approval needed");
        version.Publish(Admin, Now).Should().BeFalse("a published version cannot be published again");
    }

    [Fact]
    public void ActivatingAPublishedVersion_SyncsLegacyInAppColumns()
    {
        var template = new NotificationTemplate { Id = Guid.NewGuid(), Code = "work_approval_requested", EventKey = "work_approval_requested" };
        var version = NotificationTemplateVersion.CreateDraft(template.Id, 1, Content("New wording"), Admin, Now);
        version.Publish(Admin, Now);

        template.Activate(version, Now);

        template.ActiveVersionId.Should().Be(version.Id);
        template.InAppTitleTemplate.Should().Be("New wording");
        template.InAppBodyTemplate.Should().Be("{{actorName}} needs you");
    }

    [Fact]
    public void ActivatingADraft_IsRejected()
    {
        var template = new NotificationTemplate { Id = Guid.NewGuid(), Code = "x", EventKey = "x" };
        var draft = NotificationTemplateVersion.CreateDraft(template.Id, 1, Content(), Admin, Now);

        var act = () => template.Activate(draft, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RestoreToDraft_CopiesContentIntoANewDraft()
    {
        var old = NotificationTemplateVersion.CreateDraft(Guid.NewGuid(), 1, Content("Old wording"), Admin, Now);
        old.Publish(Admin, Now);

        var restored = NotificationTemplateVersion.CreateDraft(old.TemplateId, 5, old.ToContent(), Admin, Now);

        restored.Version.Should().Be(5);
        restored.Status.Should().Be(NotificationTemplateVersionStatus.Draft);
        restored.InAppTitle.Should().Be("Old wording");
        old.Status.Should().Be(NotificationTemplateVersionStatus.Published);
    }
}
