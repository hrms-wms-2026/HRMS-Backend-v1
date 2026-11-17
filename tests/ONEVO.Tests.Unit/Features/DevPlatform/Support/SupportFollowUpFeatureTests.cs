using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Admin;
using ONEVO.Application.Features.DevPlatform.Support.Announcements;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Application.Features.DevPlatform.Support.Tenant;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Infrastructure.Services.DevPlatform.Support;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

/// <summary>Notifications, the awaiting-support flag, ticket edits and the announcement display window.</summary>
public sealed class SupportFollowUpFeatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TenantUserId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();

    private readonly Mock<ISupportTicketRepository> _repo = new();
    private readonly Mock<ISupportNotifier> _notifier = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    public SupportFollowUpFeatureTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(TenantUserId);
        _currentUser.SetupGet(c => c.Email).Returns("owner@acme.test");
        _repo.Setup(r => r.GetTenantUserNameAsync(TenantUserId, It.IsAny<CancellationToken>())).ReturnsAsync("Anu Owner");
        _repo.Setup(r => r.GetPlatformUserNameAsync(StaffId, It.IsAny<CancellationToken>())).ReturnsAsync("Priya Support");
        _repo.Setup(r => r.GetTenantNameAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync("Acme");
    }

    private SupportTicket Ticket(string status = SupportTicket.StatusOpen, bool awaiting = false) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, Subject = "Payroll export", Description = "Empty", Status = status,
        Priority = SupportTicket.PriorityMedium, CreatedByUserId = TenantUserId, CreatedByName = "Anu Owner",
        AwaitingSupport = awaiting,
    };

    // ---- awaiting support + notifications from handlers ----------------------------------------

    [Fact]
    public async Task TenantRaisingATicket_MarksItAwaitingSupport_AndAlertsSupportAfterSaving()
    {
        var order = new List<string>();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("save")).ReturnsAsync(1);
        SupportTicket? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<SupportTicket>(), It.IsAny<CancellationToken>()))
            .Callback<SupportTicket, CancellationToken>((t, _) => added = t);
        _notifier.Setup(n => n.AlertSupportAsync(It.IsAny<SupportTicket>(), SupportAlertKind.TicketRaised, "Anu Owner", "Empty export", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("alert")).Returns(Task.CompletedTask);
        var handler = new CreateTenantSupportTicketCommandHandler(_repo.Object, _notifier.Object, _currentUser.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(new CreateTenantSupportTicketCommand("Payroll", "Empty export", "high", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.AwaitingSupport.Should().BeTrue();
        order.Should().Equal("save", "alert");
    }

    [Fact]
    public async Task TenantReply_MarksAwaitingSupport_AndAlertsSupport()
    {
        var ticket = Ticket();
        _repo.Setup(r => r.GetAsync(ticket.Id, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        var handler = new AddTenantSupportTicketCommentCommandHandler(_repo.Object, _notifier.Object, _currentUser.Object, _uow.Object, _clock.Object);

        await handler.Handle(new AddTenantSupportTicketCommentCommand(ticket.Id, "Any news?"), CancellationToken.None);

        ticket.AwaitingSupport.Should().BeTrue();
        _notifier.Verify(n => n.AlertSupportAsync(ticket, SupportAlertKind.TenantReplied, "Anu Owner", "Any news?", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SupportReply_ClearsAwaitingSupport_AndNotifiesTenantBeforeSaving()
    {
        var ticket = Ticket(awaiting: true);
        _repo.Setup(r => r.GetAsync(ticket.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        var order = new List<string>();
        _notifier.Setup(n => n.QueueTenantReplyAsync(ticket, It.IsAny<SupportTicketComment>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("queue")).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("save")).ReturnsAsync(1);
        var handler = new AddSupportTicketCommentCommandHandler(_repo.Object, _notifier.Object, _uow.Object, _clock.Object);

        await handler.Handle(new AddSupportTicketCommentCommand(ticket.Id, "Fixed now", false, StaffId), CancellationToken.None);

        ticket.AwaitingSupport.Should().BeFalse();
        order.Should().Equal("queue", "save");
    }

    [Fact]
    public async Task InternalNote_NeitherClearsAwaitingSupport_NorNotifiesTenant()
    {
        var ticket = Ticket(awaiting: true);
        _repo.Setup(r => r.GetAsync(ticket.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        var handler = new AddSupportTicketCommentCommandHandler(_repo.Object, _notifier.Object, _uow.Object, _clock.Object);

        await handler.Handle(new AddSupportTicketCommentCommand(ticket.Id, "Checking logs", true, StaffId), CancellationToken.None);

        ticket.AwaitingSupport.Should().BeTrue();
        _notifier.Verify(n => n.QueueTenantReplyAsync(It.IsAny<SupportTicket>(), It.IsAny<SupportTicketComment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(SupportTicket.StatusResolved, true)]
    [InlineData(SupportTicket.StatusInProgress, true)]
    [InlineData(SupportTicket.StatusOpen, false)]
    public async Task StatusChange_NotifiesTenant_ExceptOnReopen(string newStatus, bool notifies)
    {
        var ticket = Ticket(newStatus == SupportTicket.StatusOpen ? SupportTicket.StatusResolved : SupportTicket.StatusOpen, awaiting: true);
        _repo.Setup(r => r.GetAsync(ticket.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        var handler = new UpdateSupportTicketStatusCommandHandler(_repo.Object, _notifier.Object, _uow.Object, _clock.Object);

        await handler.Handle(new UpdateSupportTicketStatusCommand(ticket.Id, newStatus), CancellationToken.None);

        _notifier.Verify(n => n.QueueTenantStatusChangeAsync(ticket, It.IsAny<CancellationToken>()), notifies ? Times.Once() : Times.Never());
        if (newStatus == SupportTicket.StatusResolved)
        {
            ticket.AwaitingSupport.Should().BeFalse();
        }
    }

    // ---- ticket edits ---------------------------------------------------------------------------

    [Fact]
    public async Task AdminEdit_ReplacesTicketFields()
    {
        var ticket = Ticket();
        _repo.Setup(r => r.GetAsync(ticket.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        var handler = new UpdateSupportTicketCommandHandler(_repo.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(
            new UpdateSupportTicketCommand(ticket.Id, " Payroll export empty ", "October export has no rows", "urgent", " payroll "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Subject.Should().Be("Payroll export empty");
        ticket.Priority.Should().Be("urgent");
        ticket.Category.Should().Be("payroll");
        ticket.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task TenantEdit_IsScopedToTheCallersTenant_AndRefusedOnceResolved()
    {
        var ticket = Ticket(SupportTicket.StatusResolved);
        _repo.Setup(r => r.GetAsync(ticket.Id, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        var handler = new UpdateTenantSupportTicketCommandHandler(_repo.Object, _currentUser.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(
            new UpdateTenantSupportTicketCommand(ticket.Id, "New subject", "New text", "low", null), CancellationToken.None);

        result.StatusCode.Should().Be(409);
        ticket.Subject.Should().Be("Payroll export");
    }

    [Fact]
    public async Task TenantEdit_UpdatesAnOpenTicket()
    {
        var ticket = Ticket();
        _repo.Setup(r => r.GetAsync(ticket.Id, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        var handler = new UpdateTenantSupportTicketCommandHandler(_repo.Object, _currentUser.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(
            new UpdateTenantSupportTicketCommand(ticket.Id, "New subject", "New text", "high", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Subject.Should().Be("New subject");
        ticket.Priority.Should().Be("high");
    }

    // ---- summary --------------------------------------------------------------------------------

    [Fact]
    public async Task Summary_ReturnsCountsForTheCaller()
    {
        _repo.Setup(r => r.CountAsync(StaffId, It.IsAny<CancellationToken>())).ReturnsAsync(new SupportCounts(5, 2, 3));
        var handler = new GetSupportSummaryQueryHandler(_repo.Object);

        var result = await handler.Handle(new GetSupportSummaryQuery(StaffId), CancellationToken.None);

        result.Value!.AwaitingSupport.Should().Be(5);
        result.Value.AwaitingSupportAssignedToMe.Should().Be(2);
        result.Value.OpenUnassigned.Should().Be(3);
    }

    // ---- announcements ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAnnouncement_RejectsAWindowThatEndsBeforeItStarts()
    {
        var handler = new CreatePlatformAnnouncementCommandHandler(
            new Mock<IPlatformAnnouncementRepository>().Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(
            new CreatePlatformAnnouncementCommand("Maintenance", "Sunday", "info", "all", Now.AddDays(2), Now.AddDays(1), StaffId),
            CancellationToken.None);

        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("ends_at");
    }

    [Fact]
    public async Task UpdateAnnouncement_ReplacesTextAndWindow()
    {
        var announcement = new PlatformAnnouncement { Id = Guid.NewGuid(), Title = "Old", Body = "Old", IsPublished = true };
        var repo = new Mock<IPlatformAnnouncementRepository>();
        repo.Setup(r => r.GetAsync(announcement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(announcement);
        var handler = new UpdatePlatformAnnouncementCommandHandler(repo.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(
            new UpdatePlatformAnnouncementCommand(announcement.Id, "Maintenance", "Sunday 02:00", "warning", "tenants", Now, Now.AddHours(4)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        announcement.Title.Should().Be("Maintenance");
        announcement.Audience.Should().Be("tenants");
        announcement.EndsAt.Should().Be(Now.AddHours(4));
        announcement.IsPublished.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAnnouncement_RemovesIt_OrReturnsNotFound()
    {
        var announcement = new PlatformAnnouncement { Id = Guid.NewGuid() };
        var repo = new Mock<IPlatformAnnouncementRepository>();
        repo.Setup(r => r.GetAsync(announcement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(announcement);
        var handler = new DeletePlatformAnnouncementCommandHandler(repo.Object, _uow.Object);

        (await handler.Handle(new DeletePlatformAnnouncementCommand(announcement.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();
        repo.Verify(r => r.Remove(announcement), Times.Once);

        (await handler.Handle(new DeletePlatformAnnouncementCommand(Guid.NewGuid()), CancellationToken.None)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ActiveAnnouncements_AreReadAtTheCurrentTime()
    {
        var repo = new Mock<IPlatformAnnouncementRepository>();
        repo.Setup(r => r.ListActiveAsync(PlatformAnnouncement.AudienceTenants, Now, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlatformAnnouncement { Id = Guid.NewGuid(), Title = "Hi", Body = "There", Severity = "info" }]);
        var handler = new GetActiveAnnouncementsQueryHandler(repo.Object, _clock.Object);

        var result = await handler.Handle(new GetActiveAnnouncementsQuery(PlatformAnnouncement.AudienceTenants), CancellationToken.None);

        result.Value.Should().ContainSingle();
    }

    // ---- notifier --------------------------------------------------------------------------------

    private (SupportNotifier Notifier, Mock<INotificationDispatcher> Dispatcher, Mock<IPermissionRepository> Permissions, Mock<IEmailService> Email)
        Notifier(string alertEmail = "support@crownus.test")
    {
        var dispatcher = new Mock<INotificationDispatcher>();
        var permissions = new Mock<IPermissionRepository>();
        var email = new Mock<IEmailService>();
        var notifier = new SupportNotifier(
            dispatcher.Object, permissions.Object, _repo.Object, email.Object, _clock.Object,
            Options.Create(new SupportAlertOptions { AlertEmail = alertEmail }),
            Options.Create(new EmailOptions { AdminConsoleBaseUrl = "https://admin.crownus.test" }),
            NullLogger<SupportNotifier>.Instance);
        return (notifier, dispatcher, permissions, email);
    }

    [Fact]
    public void SupportEvents_AreRegistered_AndOwnedByTheOrchestrator()
    {
        foreach (var key in new[] { SupportNotifier.ReplyEventKey, SupportNotifier.StatusEventKey })
        {
            NotificationEventCatalog.TryGet(key, out var definition).Should().BeTrue();
            definition.OrchestratorOwned.Should().BeTrue();
            definition.Module.Should().Be("support");
        }
    }

    [Fact]
    public async Task TenantReplyNotice_GoesToTheCreatorAndTenantCommenters_Once()
    {
        var ticket = Ticket();
        var commenter = Guid.NewGuid();
        _repo.Setup(r => r.ListTenantCommenterIdsAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync([commenter, TenantUserId]);
        var (notifier, dispatcher, permissions, _) = Notifier();

        await notifier.QueueTenantReplyAsync(ticket, new SupportTicketComment { AuthorName = "Priya", IsInternal = false }, CancellationToken.None);

        dispatcher.Verify(d => d.SendTemplatedAsync(TenantId, TenantUserId, SupportNotifier.ReplyEventKey,
            It.Is<IReadOnlyDictionary<string, string>>(p => p["authorName"] == "Priya" && p["subject"] == "Payroll export"),
            SupportNotifier.RelatedEntityType, ticket.Id, It.IsAny<CancellationToken>()), Times.Once);
        dispatcher.Verify(d => d.SendTemplatedAsync(TenantId, commenter, SupportNotifier.ReplyEventKey,
            It.IsAny<IReadOnlyDictionary<string, string>>(), SupportNotifier.RelatedEntityType, ticket.Id, It.IsAny<CancellationToken>()), Times.Once);
        permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TicketOpenedBySupport_NotifiesEveryoneWhoManagesSupport()
    {
        var ticket = Ticket();
        ticket.CreatedByUserId = null;
        ticket.CreatedByPlatformUserId = StaffId;
        _repo.Setup(r => r.ListTenantCommenterIdsAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var manager = Guid.NewGuid();
        var (notifier, dispatcher, permissions, _) = Notifier();
        permissions.Setup(p => p.ListUserIdsWithPermissionCodeAsync(TenantId, "support:manage", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([manager]);

        await notifier.QueueTenantStatusChangeAsync(ticket, CancellationToken.None);

        dispatcher.Verify(d => d.SendTemplatedAsync(TenantId, manager, SupportNotifier.StatusEventKey,
            It.Is<IReadOnlyDictionary<string, string>>(p => p["status"] == "open"),
            SupportNotifier.RelatedEntityType, ticket.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InternalNote_NeverReachesTheTenant()
    {
        var (notifier, dispatcher, _, _) = Notifier();

        await notifier.QueueTenantReplyAsync(Ticket(), new SupportTicketComment { IsInternal = true }, CancellationToken.None);

        dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NotificationFailure_IsSwallowed()
    {
        var ticket = Ticket();
        _repo.Setup(r => r.ListTenantCommenterIdsAsync(ticket.Id, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var (notifier, _, _, _) = Notifier();

        var act = () => notifier.QueueTenantStatusChangeAsync(ticket, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SupportAlert_GoesToTheAssignee_WithEncodedContentAndAConsoleLink()
    {
        var ticket = Ticket();
        ticket.AssignedToPlatformUserId = StaffId;
        _repo.Setup(r => r.GetActivePlatformUserEmailAsync(StaffId, It.IsAny<CancellationToken>())).ReturnsAsync("priya@crownus.test");
        var (notifier, _, _, email) = Notifier();

        await notifier.AlertSupportAsync(ticket, SupportAlertKind.TenantReplied, "Anu", "<script>x</script>", CancellationToken.None);

        email.Verify(e => e.SendAsync("priya@crownus.test", "Acme replied: Payroll export",
            It.Is<string>(html => html.Contains("&lt;script&gt;") && !html.Contains("<script>")
                && html.Contains($"https://admin.crownus.test/support/tickets/{ticket.Id}")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SupportAlert_ForUnassignedTicket_GoesToTheSupportInbox_OrIsSkipped()
    {
        var ticket = Ticket();
        var (withInbox, _, _, inboxEmail) = Notifier("support@crownus.test");
        await withInbox.AlertSupportAsync(ticket, SupportAlertKind.TicketRaised, "Anu", "Help", CancellationToken.None);
        inboxEmail.Verify(e => e.SendAsync("support@crownus.test", "New support ticket from Acme: Payroll export",
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        var (withoutInbox, _, _, noEmail) = Notifier(alertEmail: "");
        await withoutInbox.AlertSupportAsync(ticket, SupportAlertKind.TicketRaised, "Anu", "Help", CancellationToken.None);
        noEmail.VerifyNoOtherCalls();
    }
}
