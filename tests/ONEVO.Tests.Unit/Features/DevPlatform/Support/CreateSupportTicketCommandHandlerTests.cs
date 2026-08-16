using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Commands.CreateSupportTicket;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class CreateSupportTicketCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly Mock<ISupportTicketRepository> _tickets = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private static readonly Guid ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public CreateSupportTicketCommandHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private CreateSupportTicketCommandHandler BuildSut() => new(_tickets.Object, _uow.Object, _clock.Object);

    [Fact]
    public async Task Handle_HappyPath_CreatesOpenMediumPriorityTicket()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateSupportTicketCommand(null, "Cannot log in", "User gets 500 on login.", null, null, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(SupportTicket.StatusOpen);
        result.Value!.Priority.Should().Be(SupportTicket.PriorityMedium);
        result.Value!.CreatedAt.Should().Be(Now);
        _tickets.Verify(t => t.AddAsync(It.IsAny<SupportTicket>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmptySubject_ReturnsBadRequest()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateSupportTicketCommand(null, "   ", "Description", null, null, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _tickets.Verify(t => t.AddAsync(It.IsAny<SupportTicket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SubjectOverMaxLength_ReturnsBadRequest()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateSupportTicketCommand(null, new string('a', 201), "Description", null, null, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_DescriptionOverMaxLength_ReturnsBadRequest()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateSupportTicketCommand(null, "Subject", new string('a', 4001), null, null, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_UnknownPriority_ReturnsBadRequest()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateSupportTicketCommand(null, "Subject", "Description", "not_a_priority", null, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_ValidTenantAndPriority_PersistsTenantIdAndPriority()
    {
        var tenantId = Guid.NewGuid();
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateSupportTicketCommand(tenantId, "Subject", "Description", SupportTicket.PriorityUrgent, "billing", ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TenantId.Should().Be(tenantId);
        result.Value!.Priority.Should().Be(SupportTicket.PriorityUrgent);
        result.Value!.Category.Should().Be("billing");
    }
}
