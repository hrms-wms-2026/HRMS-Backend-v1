using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Support.Queries.ListSupportTickets;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class ListSupportTicketsQueryHandlerTests
{
    private readonly Mock<ISupportTicketRepository> _tickets = new();

    private ListSupportTicketsQueryHandler BuildSut() => new(_tickets.Object);

    [Fact]
    public async Task Handle_DefaultsPageAndPageSize_WhenNotPositive()
    {
        _tickets.Setup(t => t.ListAsync(null, null, null, 0, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket>());
        _tickets.Setup(t => t.CountAsync(null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var sut = BuildSut();
        var result = await sut.Handle(new ListSupportTicketsQuery(null, null, null, 0, 0), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value!.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task Handle_CapsPageSizeAtOneHundred()
    {
        _tickets.Setup(t => t.ListAsync(null, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket>());
        _tickets.Setup(t => t.CountAsync(null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var sut = BuildSut();
        var result = await sut.Handle(new ListSupportTicketsQuery(null, null, null, 1, 500), CancellationToken.None);

        result.Value!.PageSize.Should().Be(100);
        _tickets.Verify(t => t.ListAsync(null, null, null, 0, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PassesStatusPriorityAndTenantFilters_ToRepository()
    {
        var tenantId = Guid.NewGuid();
        _tickets.Setup(t => t.ListAsync(SupportTicket.StatusOpen, SupportTicket.PriorityHigh, tenantId, 25, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupportTicket>());
        _tickets.Setup(t => t.CountAsync(SupportTicket.StatusOpen, SupportTicket.PriorityHigh, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ListSupportTicketsQuery(SupportTicket.StatusOpen, SupportTicket.PriorityHigh, tenantId, 2, 25),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(3);
        _tickets.Verify(
            t => t.ListAsync(SupportTicket.StatusOpen, SupportTicket.PriorityHigh, tenantId, 25, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
