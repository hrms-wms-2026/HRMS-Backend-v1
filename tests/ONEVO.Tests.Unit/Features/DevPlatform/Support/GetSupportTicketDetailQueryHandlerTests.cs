using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Support.Queries.GetSupportTicketDetail;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class GetSupportTicketDetailQueryHandlerTests
{
    private readonly Mock<ISupportTicketRepository> _tickets = new();

    private GetSupportTicketDetailQueryHandler BuildSut() => new(_tickets.Object);

    [Fact]
    public async Task Handle_UnknownTicket_ReturnsNotFound()
    {
        _tickets.Setup(t => t.GetByIdWithCommentsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupportTicket?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new GetSupportTicketDetailQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ReturnsCommentsOrderedOldestFirst()
    {
        var ticket = new SupportTicket { Id = Guid.NewGuid(), Subject = "S", Description = "D" };
        var newer = new SupportTicketComment
        {
            Id = Guid.NewGuid(), TicketId = ticket.Id, Body = "Second",
            CreatedAt = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
        };
        var older = new SupportTicketComment
        {
            Id = Guid.NewGuid(), TicketId = ticket.Id, Body = "First",
            CreatedAt = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero),
        };
        ticket.Comments = new List<SupportTicketComment> { newer, older };

        _tickets.Setup(t => t.GetByIdWithCommentsAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var sut = BuildSut();
        var result = await sut.Handle(new GetSupportTicketDetailQuery(ticket.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Comments.Should().HaveCount(2);
        result.Value!.Comments[0].Body.Should().Be("First");
        result.Value!.Comments[1].Body.Should().Be("Second");
    }
}
