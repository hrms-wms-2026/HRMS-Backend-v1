using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Announcements;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Infrastructure.Persistence.Rls;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class SupportAttachmentAndAnnouncementTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static Mock<IDateTimeProvider> Clock()
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        return clock;
    }

    [Fact]
    public async Task Store_RejectedUpload_NeverWritesToStorage()
    {
        var policy = new Mock<IUploadPurposePolicy>();
        policy.Setup(p => p.ValidateUpload(UploadPurposeCatalog.SupportTicketAttachment, "run.exe", It.IsAny<string>(), 10))
            .Returns(Result.Failure("File type not allowed.", 415));
        var storage = new Mock<IObjectStorageAdapter>();
        var service = new SupportAttachmentService(policy.Object, storage.Object, Clock().Object, new ONEVO.Tests.Unit.Fakes.FakeImageSanitizer());
        var ticket = new SupportTicket { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };

        var result = await service.StoreAsync(
            ticket, new SupportUpload(new MemoryStream(new byte[10]), "run.exe", "application/x-msdownload", 10),
            new SupportUploader(Guid.NewGuid(), null, "Priya"), false, CancellationToken.None);

        result.StatusCode.Should().Be(415);
        storage.Verify(s => s.PutObjectAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Store_ValidUpload_WritesUnderTenantAndTicketPrefix()
    {
        var policy = new Mock<IUploadPurposePolicy>();
        policy.Setup(p => p.ValidateUpload(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
            .Returns(Result.Success());
        policy.Setup(p => p.ValidateSignature(It.IsAny<string>(), It.IsAny<Stream>())).Returns(Result.Success());
        policy.Setup(p => p.SanitizeFileName("my log.pdf")).Returns("my_log.pdf");
        var storage = new Mock<IObjectStorageAdapter>();
        var service = new SupportAttachmentService(policy.Object, storage.Object, Clock().Object, new ONEVO.Tests.Unit.Fakes.FakeImageSanitizer());
        var ticket = new SupportTicket { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        var tenantUser = Guid.NewGuid();

        var result = await service.StoreAsync(
            ticket, new SupportUpload(new MemoryStream(new byte[10]), "my log.pdf", "application/pdf", 10),
            new SupportUploader(null, tenantUser, "Anu"), false, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var attachment = result.Value!;
        attachment.ObjectKey.Should().StartWith($"support-tickets/{ticket.TenantId:N}/{ticket.Id:N}/{attachment.Id:N}/")
            .And.EndWith("/content.pdf"); // Task 23: server-generated object name
        attachment.FileName.Should().Be("my_log.pdf");
        attachment.TenantId.Should().Be(ticket.TenantId);
        attachment.UploadedByUserId.Should().Be(tenantUser);
        storage.Verify(s => s.PutObjectAsync(attachment.ObjectKey, It.IsAny<Stream>(), "application/pdf", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void SupportTicketAttachment_IsARegisteredUploadPurpose()
    {
        UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.SupportTicketAttachment).Should().BeTrue();
        UploadPurposeCatalog.GetRule(UploadPurposeCatalog.SupportTicketAttachment)!.MaxSizeBytes.Should().Be(25 * 1024 * 1024);
    }

    [Fact]
    public void SupportTables_AreUnderTheTenantRule_OutsideTheBaseline()
    {
        foreach (var table in new[] { "support_tickets", "support_ticket_comments", "support_ticket_attachments" })
        {
            RlsTableRegistry.Tables[table].Should().Be(RlsPredicate.Tenant);
            RlsTableRegistry.Baseline.Should().NotContainKey(table);
        }

        RlsTableRegistry.Tables.Count.Should().Be(RlsTableRegistry.Baseline.Count + RlsTableRegistry.AddedAfterBaseline.Count);
    }

    [Fact]
    public async Task ActiveAnnouncements_RejectsAudienceOtherThanTenantsOrAdmins()
    {
        var handler = new GetActiveAnnouncementsQueryHandler(new Mock<IPlatformAnnouncementRepository>().Object, Clock().Object);

        var result = await handler.Handle(new GetActiveAnnouncementsQuery(PlatformAnnouncement.AudienceAll), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Publish_SetsPublishedFields_AndUnpublishKeepsHistory()
    {
        var announcement = new PlatformAnnouncement { Id = Guid.NewGuid(), Title = "Maintenance", Body = "Sunday" };
        var repo = new Mock<IPlatformAnnouncementRepository>();
        repo.Setup(r => r.GetAsync(announcement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(announcement);
        var actor = Guid.NewGuid();
        var handler = new SetPlatformAnnouncementPublishedCommandHandler(repo.Object, new Mock<IUnitOfWork>().Object, Clock().Object);

        (await handler.Handle(new SetPlatformAnnouncementPublishedCommand(announcement.Id, true, actor), CancellationToken.None))
            .Value!.IsPublished.Should().BeTrue();
        announcement.PublishedAt.Should().Be(Now);
        announcement.PublishedByPlatformUserId.Should().Be(actor);

        (await handler.Handle(new SetPlatformAnnouncementPublishedCommand(announcement.Id, false, actor), CancellationToken.None))
            .Value!.IsPublished.Should().BeFalse();
        announcement.PublishedAt.Should().Be(Now);
    }

    [Fact]
    public async Task CreateAnnouncement_RejectsUnknownAudience()
    {
        var handler = new CreatePlatformAnnouncementCommandHandler(
            new Mock<IPlatformAnnouncementRepository>().Object, new Mock<IUnitOfWork>().Object, Clock().Object);

        var result = await handler.Handle(
            new CreatePlatformAnnouncementCommand("Title", "Body", "info", "everyone", null, null, Guid.NewGuid()), CancellationToken.None);

        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("audience");
    }

    [Fact]
    public async Task GetAnnouncement_ReturnsTheRow_AndNotFoundForAnUnknownId()
    {
        var announcement = new PlatformAnnouncement
        {
            Id = Guid.NewGuid(), Title = "Maintenance", Body = "Sunday", Severity = "warning", Audience = "tenants",
            StartsAt = Now, EndsAt = Now.AddHours(4)
        };
        var repo = new Mock<IPlatformAnnouncementRepository>();
        repo.Setup(r => r.GetAsync(announcement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(announcement);
        var handler = new GetPlatformAnnouncementQueryHandler(repo.Object);

        var found = await handler.Handle(new GetPlatformAnnouncementQuery(announcement.Id), CancellationToken.None);
        var missing = await handler.Handle(new GetPlatformAnnouncementQuery(Guid.NewGuid()), CancellationToken.None);

        found.IsSuccess.Should().BeTrue();
        found.Value!.Title.Should().Be("Maintenance");
        found.Value.EndsAt.Should().Be(Now.AddHours(4));
        missing.StatusCode.Should().Be(404);
    }
}
