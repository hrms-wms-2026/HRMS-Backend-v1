using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.ArchiveSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Subscription;

public class ArchiveSubscriptionPlanCommandHandlerTests
{
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ArchiveSubscriptionPlanCommandHandler _handler;

    public ArchiveSubscriptionPlanCommandHandlerTests()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _handler = new ArchiveSubscriptionPlanCommandHandler(_planRepo, _unitOfWork, _clock);
    }

    [Fact]
    public async Task Handle_PlanNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _planRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        var result = await _handler.Handle(new ArchiveSubscriptionPlanCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingPlan_SetsIsActiveFalseAndSaves()
    {
        var id = Guid.NewGuid();
        var plan = new SubscriptionPlan { Id = id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        _planRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(plan);

        var result = await _handler.Handle(new ArchiveSubscriptionPlanCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        plan.IsActive.Should().BeFalse();
        plan.UpdatedAt.Should().Be(_clock.UtcNow);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
