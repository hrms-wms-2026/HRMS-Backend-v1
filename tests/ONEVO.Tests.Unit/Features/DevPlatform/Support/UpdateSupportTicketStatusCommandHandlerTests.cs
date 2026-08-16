using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Commands.UpdateSupportTicketStatus;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class UpdateSupportTicketStatusCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 15, 0, 0, TimeSpan.Zero);
    private readonly Mock<ISupportTicketRepository> _tickets = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public UpdateSupportTicketStatusCommandHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private UpdateSupportTicketStatusCommandHandler BuildSut() => new(_tickets.Object, _uow.Object, _clock.Object);

    [Fact]
    public async Task Handle_UnknownStatus_ReturnsBadRequest()
    {
        var sut = BuildSut();
        var result = await sut.Handle(
            new UpdateSupportTicketStatusCommand(Guid.NewGuid(), "not_a_status"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_UnknownTicket_ReturnsNotFound()
    {
        _tickets.Setup(t => t.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupportTicket?)null);

        var sut = BuildSut();
        var result = await sut.Handle(
            new UpdateSupportTicketStatusCommand(Guid.NewGuid(), SupportTicket.StatusInProgress), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_TransitioningToResolved_SetsResolvedAt()
    {
        var ticket = new SupportTicket { Id = Guid.NewGuid(), Status = SupportTicket.StatusOpen, ResolvedAt = null };
        _tickets.Setup(t => t.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var sut = BuildSut();
        var result = await sut.Handle(
            new UpdateSupportTicketStatusCommand(ticket.Id, SupportTicket.StatusResolved), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(SupportTicket.StatusResolved);
        ticket.ResolvedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Handle_AlreadyResolved_DoesNotOverwriteOriginalResolvedAt()
    {
        var originallyResolvedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(), Status = SupportTicket.StatusResolved, ResolvedAt = originallyResolvedAt,
        };
        _tickets.Setup(t => t.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var sut = BuildSut();
        await sut.Handle(new UpdateSupportTicketStatusCommand(ticket.Id, SupportTicket.StatusResolved), CancellationToken.None);

        ticket.ResolvedAt.Should().Be(originallyResolvedAt);
    }

    [Theory]
    [InlineData(SupportTicket.StatusResolved)]
    [InlineData(SupportTicket.StatusClosed)]
    public async Task Handle_ReopeningFromResolvedOrClosed_ClearsResolvedAt(string startingStatus)
    {
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(), Status = startingStatus,
            ResolvedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        };
        _tickets.Setup(t => t.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var sut = BuildSut();
        var result = await sut.Handle(
            new UpdateSupportTicketStatusCommand(ticket.Id, SupportTicket.StatusOpen), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(SupportTicket.StatusOpen);
        ticket.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ClosedToResolved_KeepsExistingResolvedAt()
    {
        var originallyResolvedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(), Status = SupportTicket.StatusClosed, ResolvedAt = originallyResolvedAt,
        };
        _tickets.Setup(t => t.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var sut = BuildSut();
        await sut.Handle(new UpdateSupportTicketStatusCommand(ticket.Id, SupportTicket.StatusResolved), CancellationToken.None);

        ticket.Status.Should().Be(SupportTicket.StatusResolved);
        ticket.ResolvedAt.Should().Be(originallyResolvedAt);
    }
}
