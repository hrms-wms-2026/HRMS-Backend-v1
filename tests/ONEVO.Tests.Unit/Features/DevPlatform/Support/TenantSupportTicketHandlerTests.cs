using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Application.Features.DevPlatform.Support.Tenant;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class TenantSupportTicketHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ISupportTicketRepository> _repo = new();
    private readonly Mock<ISupportNotifier> _notifier = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public TenantSupportTicketHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        _currentUser.SetupGet(c => c.Email).Returns("owner@acme.test");
        _repo.Setup(r => r.GetTenantUserNameAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync("Anu Owner");
    }

    [Fact]
    public async Task Create_AlwaysUsesTheCallersTenant_AndTenantCreator()
    {
        SupportTicket? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<SupportTicket>(), It.IsAny<CancellationToken>()))
            .Callback<SupportTicket, CancellationToken>((t, _) => added = t);
        var handler = new CreateTenantSupportTicketCommandHandler(_repo.Object, _notifier.Object, _currentUser.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(
            new CreateTenantSupportTicketCommand("Payroll export", "Export is empty", "medium", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.TenantId.Should().Be(TenantId);
        added.CreatedByUserId.Should().Be(UserId);
        added.CreatedByPlatformUserId.Should().BeNull();
        added.CreatedByName.Should().Be("Anu Owner");
        result.Value!.RaisedBySupport.Should().BeFalse();
    }

    [Fact]
    public async Task Get_ScopesLookupToTheCallersTenant()
    {
        var handler = new GetTenantSupportTicketQueryHandler(_repo.Object, _currentUser.Object);
        var ticketId = Guid.NewGuid();

        var result = await handler.Handle(new GetTenantSupportTicketQuery(ticketId), CancellationToken.None);

        result.StatusCode.Should().Be(404);
        _repo.Verify(r => r.GetWithChildrenAsync(ticketId, TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TenantDetail_NeverIncludesInternalNotesOrFiles()
    {
        var ticket = new SupportTicket { Id = Guid.NewGuid(), TenantId = TenantId, CreatedByUserId = UserId };
        ticket.Comments.Add(new SupportTicketComment { Id = Guid.NewGuid(), Body = "public", AuthorPlatformUserId = Guid.NewGuid() });
        ticket.Comments.Add(new SupportTicketComment { Id = Guid.NewGuid(), Body = "secret", IsInternal = true, AuthorPlatformUserId = Guid.NewGuid() });
        ticket.Attachments.Add(new SupportTicketAttachment { Id = Guid.NewGuid(), FileName = "shared.pdf" });
        ticket.Attachments.Add(new SupportTicketAttachment { Id = Guid.NewGuid(), FileName = "internal.pdf", IsInternal = true });

        var dto = SupportMapper.ToTenantDetailDto(ticket);

        dto.Comments.Should().ContainSingle().Which.Body.Should().Be("public");
        dto.Comments[0].IsFromSupport.Should().BeTrue();
        dto.Attachments.Should().ContainSingle().Which.FileName.Should().Be("shared.pdf");
    }

    [Fact]
    public async Task Comment_OnClosedTicket_IsRejected()
    {
        var ticket = new SupportTicket { Id = Guid.NewGuid(), TenantId = TenantId, Status = SupportTicket.StatusClosed };
        _repo.Setup(r => r.GetAsync(ticket.Id, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        var handler = new AddTenantSupportTicketCommentCommandHandler(_repo.Object, _notifier.Object, _currentUser.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(new AddTenantSupportTicketCommentCommand(ticket.Id, "Still broken"), CancellationToken.None);

        result.StatusCode.Should().Be(409);
        _repo.Verify(r => r.AddCommentAsync(It.IsAny<SupportTicketComment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Comment_OnResolvedTicket_ReopensIt()
    {
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Status = SupportTicket.StatusResolved, ResolvedAt = Now.AddDays(-1)
        };
        _repo.Setup(r => r.GetAsync(ticket.Id, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        SupportTicketComment? added = null;
        _repo.Setup(r => r.AddCommentAsync(It.IsAny<SupportTicketComment>(), It.IsAny<CancellationToken>()))
            .Callback<SupportTicketComment, CancellationToken>((c, _) => added = c);
        var handler = new AddTenantSupportTicketCommentCommandHandler(_repo.Object, _notifier.Object, _currentUser.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(new AddTenantSupportTicketCommentCommand(ticket.Id, "Still broken"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(SupportTicket.StatusOpen);
        ticket.ResolvedAt.Should().BeNull();
        added!.IsInternal.Should().BeFalse();
        added.AuthorUserId.Should().Be(UserId);
        added.AuthorPlatformUserId.Should().BeNull();
    }

    [Fact]
    public async Task DownloadUrl_ForInternalAttachment_IsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var attachment = new SupportTicketAttachment { Id = Guid.NewGuid(), TicketId = ticketId, TenantId = TenantId, IsInternal = true };
        _repo.Setup(r => r.GetAttachmentAsync(ticketId, attachment.Id, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attachment);
        var storage = new Mock<ISupportAttachmentService>();
        var handler = new GetTenantSupportAttachmentDownloadUrlQueryHandler(_repo.Object, storage.Object, _currentUser.Object);

        var result = await handler.Handle(
            new GetTenantSupportAttachmentDownloadUrlQuery(ticketId, attachment.Id), CancellationToken.None);

        result.StatusCode.Should().Be(404);
        storage.Verify(s => s.GetDownloadUrlAsync(It.IsAny<SupportTicketAttachment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadUrl_ForSharedAttachment_ReturnsSignedUrl()
    {
        var ticketId = Guid.NewGuid();
        var attachment = new SupportTicketAttachment { Id = Guid.NewGuid(), TicketId = ticketId, TenantId = TenantId };
        _repo.Setup(r => r.GetAttachmentAsync(ticketId, attachment.Id, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attachment);
        var storage = new Mock<ISupportAttachmentService>();
        storage.Setup(s => s.GetDownloadUrlAsync(attachment, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportAttachmentDownloadDto("https://signed", Now.AddMinutes(5)));
        var handler = new GetTenantSupportAttachmentDownloadUrlQueryHandler(_repo.Object, storage.Object, _currentUser.Object);

        var result = await handler.Handle(
            new GetTenantSupportAttachmentDownloadUrlQuery(ticketId, attachment.Id), CancellationToken.None);

        result.Value!.Url.Should().Be("https://signed");
    }
}
