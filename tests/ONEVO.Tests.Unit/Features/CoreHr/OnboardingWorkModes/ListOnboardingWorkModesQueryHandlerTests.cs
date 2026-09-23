using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingWorkModes.Queries.ListOnboardingWorkModes;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using TimeAttendanceWorkMode = ONEVO.Domain.Features.TimeAttendance.Entities.WorkMode;

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingWorkModes;

public sealed class ListOnboardingWorkModesQueryHandlerTests
{
    private readonly Mock<IWorkModeRepository> _repository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly ListOnboardingWorkModesQueryHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    public ListOnboardingWorkModesQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(u => u.TenantId).Returns(TenantId);
        _sut = new ListOnboardingWorkModesQueryHandler(_repository.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ReturnsActiveWorkModes_MappedToDto()
    {
        var modeA = new TimeAttendanceWorkMode { Id = Guid.NewGuid(), Name = "Remote", IsActive = true };
        var modeB = new TimeAttendanceWorkMode { Id = Guid.NewGuid(), Name = "Hybrid", IsActive = true };
        _repository
            .Setup(r => r.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([modeA, modeB]);

        var result = await _sut.Handle(new ListOnboardingWorkModesQuery(LegalEntityId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].Id.Should().Be(modeA.Id);
        result.Value[0].Label.Should().Be("Remote");
        result.Value[1].Id.Should().Be(modeB.Id);
        result.Value[1].Label.Should().Be("Hybrid");
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoActiveWorkModesExist()
    {
        _repository
            .Setup(r => r.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.Handle(new ListOnboardingWorkModesQuery(LegalEntityId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ExcludesInactiveWorkModes_ByDelegatingToRepositoryWithIncludeInactiveFalse()
    {
        _repository
            .Setup(r => r.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.Handle(new ListOnboardingWorkModesQuery(LegalEntityId), CancellationToken.None);

        _repository.Verify(
            r => r.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsForbidden_WhenNotAuthenticated()
    {
        _currentUser.SetupGet(u => u.IsAuthenticated).Returns(false);

        var result = await _sut.Handle(new ListOnboardingWorkModesQuery(LegalEntityId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _repository.Verify(
            r => r.ListByLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
