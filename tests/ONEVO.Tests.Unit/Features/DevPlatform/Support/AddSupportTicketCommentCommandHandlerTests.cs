using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Commands.AddSupportTicketComment;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class AddSupportTicketCommentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 16, 0, 0, TimeSpan.Zero);
    private readonly Mock<ISupportTicketRepository> _tickets = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private static readonly Guid AuthorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public AddSupportTicketCommentCommandHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private AddSupportTicketCommentCommandHandler BuildSut() => new(_tickets.Object, _uow.Object, _clock.Object);

    [Fact]
    public async Task Handle_EmptyBody_ReturnsBadRequest()
    {
        var sut = BuildSut();
        var result = await sut.Handle(
            new AddSupportTicketCommentCommand(Guid.NewGuid(), "   ", false, AuthorId), CancellationToken.None);

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
            new AddSupportTicketCommentCommand(Guid.NewGuid(), "A reply", false, AuthorId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_HappyPath_AppendsCommentAndReturnsDto()
    {
        var ticket = new SupportTicket { Id = Guid.NewGuid(), Subject = "S", Description = "D" };
        _tickets.Setup(t => t.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var sut = BuildSut();
        var result = await sut.Handle(
            new AddSupportTicketCommentCommand(ticket.Id, "Internal note", true, AuthorId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TicketId.Should().Be(ticket.Id);
        result.Value!.Body.Should().Be("Internal note");
        result.Value!.IsInternal.Should().BeTrue();
        result.Value!.CreatedAt.Should().Be(Now);
        _tickets.Verify(
            t => t.AddCommentAsync(It.IsAny<SupportTicketComment>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
