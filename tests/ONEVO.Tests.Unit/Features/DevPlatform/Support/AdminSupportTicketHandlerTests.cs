using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Admin;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class AdminSupportTicketHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private readonly Mock<ISupportTicketRepository> _repo = new();
    private readonly Mock<ISupportNotifier> _notifier = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public AdminSupportTicketHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _repo.Setup(r => r.GetTenantNameAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync("Acme");
        _repo.Setup(r => r.GetPlatformUserNameAsync(ActorId, It.IsAny<CancellationToken>())).ReturnsAsync("Priya Support");
    }

    private CreateSupportTicketCommandHandler CreateHandler() => new(_repo.Object, _uow.Object, _clock.Object);

    [Fact]
    public async Task Create_RejectsMissingTenant()
    {
        var result = await CreateHandler().Handle(
            new CreateSupportTicketCommand(null, "Login fails", "Details", "high", null, ActorId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("tenant_id");
        _repo.Verify(r => r.AddAsync(It.IsAny<SupportTicket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ReturnsNotFound_ForUnknownTenant()
    {
        var result = await CreateHandler().Handle(
            new CreateSupportTicketCommand(Guid.NewGuid(), "Login fails", "Details", "high", null, ActorId), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_RejectsActiveContentInDescription()
    {
        var result = await CreateHandler().Handle(
            new CreateSupportTicketCommand(TenantId, "Login fails", "<script>alert(1)</script>", "high", null, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Create_StoresOpenTicketForTenant_WithPlatformCreator()
    {
        SupportTicket? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<SupportTicket>(), It.IsAny<CancellationToken>()))
            .Callback<SupportTicket, CancellationToken>((t, _) => added = t);

        var result = await CreateHandler().Handle(
            new CreateSupportTicketCommand(TenantId, "  Login fails  ", "Details", "urgent", "  Auth ", ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.TenantId.Should().Be(TenantId);
        added.Subject.Should().Be("Login fails");
        added.Category.Should().Be("Auth");
        added.Status.Should().Be(SupportTicket.StatusOpen);
        added.CreatedByPlatformUserId.Should().Be(ActorId);
        added.CreatedByUserId.Should().BeNull();
        added.CreatedByName.Should().Be("Priya Support");
        result.Value!.TenantName.Should().Be("Acme");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_ToResolved_SetsResolvedAt_AndReopenClearsIt()
    {
        var ticket = new SupportTicket { Id = Guid.NewGuid(), TenantId = TenantId, Status = SupportTicket.StatusOpen };
        _repo.Setup(r => r.GetAsync(ticket.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        var handler = new UpdateSupportTicketStatusCommandHandler(_repo.Object, _notifier.Object, _uow.Object, _clock.Object);

        (await handler.Handle(new UpdateSupportTicketStatusCommand(ticket.Id, "resolved"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        ticket.ResolvedAt.Should().Be(Now);

        (await handler.Handle(new UpdateSupportTicketStatusCommand(ticket.Id, "in_progress"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        ticket.ResolvedAt.Should().BeNull();
        ticket.Status.Should().Be(SupportTicket.StatusInProgress);
    }

    [Fact]
    public async Task UpdateStatus_RejectsUnknownStatus()
    {
        var handler = new UpdateSupportTicketStatusCommandHandler(_repo.Object, _notifier.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(new UpdateSupportTicketStatusCommand(Guid.NewGuid(), "pending"), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Assign_RejectsUserWhoIsNotAnActivePlatformUser()
    {
        var ticket = new SupportTicket { Id = Guid.NewGuid(), TenantId = TenantId };
        _repo.Setup(r => r.GetAsync(ticket.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        var handler = new AssignSupportTicketCommandHandler(_repo.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(new AssignSupportTicketCommand(ticket.Id, Guid.NewGuid()), CancellationToken.None);

        result.StatusCode.Should().Be(400);
        ticket.AssignedToPlatformUserId.Should().BeNull();
    }

    [Fact]
    public async Task Assign_SetsAndClearsAssignee()
    {
        var assignee = Guid.NewGuid();
        var ticket = new SupportTicket { Id = Guid.NewGuid(), TenantId = TenantId };
        _repo.Setup(r => r.GetAsync(ticket.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _repo.Setup(r => r.GetActivePlatformUserNameAsync(assignee, It.IsAny<CancellationToken>())).ReturnsAsync("Ravi");
        _repo.Setup(r => r.GetPlatformUserNameAsync(assignee, It.IsAny<CancellationToken>())).ReturnsAsync("Ravi");
        var handler = new AssignSupportTicketCommandHandler(_repo.Object, _uow.Object, _clock.Object);

        var assigned = await handler.Handle(new AssignSupportTicketCommand(ticket.Id, assignee), CancellationToken.None);
        assigned.Value!.AssignedToName.Should().Be("Ravi");
        ticket.AssignedToPlatformUserId.Should().Be(assignee);

        var cleared = await handler.Handle(new AssignSupportTicketCommand(ticket.Id, null), CancellationToken.None);
        cleared.Value!.AssignedToName.Should().BeNull();
        ticket.AssignedToPlatformUserId.Should().BeNull();
    }

    [Fact]
    public async Task AddComment_StoresInternalNoteAgainstTicketTenant()
    {
        var ticket = new SupportTicket { Id = Guid.NewGuid(), TenantId = TenantId };
        _repo.Setup(r => r.GetAsync(ticket.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        SupportTicketComment? added = null;
        _repo.Setup(r => r.AddCommentAsync(It.IsAny<SupportTicketComment>(), It.IsAny<CancellationToken>()))
            .Callback<SupportTicketComment, CancellationToken>((c, _) => added = c);
        var handler = new AddSupportTicketCommentCommandHandler(_repo.Object, _notifier.Object, _uow.Object, _clock.Object);

        var result = await handler.Handle(
            new AddSupportTicketCommentCommand(ticket.Id, "Checked logs", true, ActorId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.TenantId.Should().Be(TenantId);
        added.IsInternal.Should().BeTrue();
        added.AuthorPlatformUserId.Should().Be(ActorId);
        added.AuthorName.Should().Be("Priya Support");
        ticket.UpdatedAt.Should().Be(Now);
    }
}
