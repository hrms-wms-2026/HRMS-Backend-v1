using FluentAssertions;
using Moq;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.WorkModes.Queries.ListActiveWorkModes;

namespace ONEVO.Tests.Unit.Features.CoreHr.WorkMode;

public sealed class ListActiveWorkModesQueryHandlerTests
{
    private readonly Mock<IWorkModeRepository> _repository = new();
    private readonly ListActiveWorkModesQueryHandler _sut;

    public ListActiveWorkModesQueryHandlerTests()
    {
        _sut = new ListActiveWorkModesQueryHandler(_repository.Object);
    }

    [Fact]
    public async Task Handle_ReturnsActiveWorkModes_MappedToDto()
    {
        _repository
            .Setup(r => r.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ONEVO.Domain.Lookups.WorkMode { Id = 1, Code = "on_site", Label = "On-Site", IsActive = true },
                new ONEVO.Domain.Lookups.WorkMode { Id = 2, Code = "remote", Label = "Remote", IsActive = true },
            ]);

        var result = await _sut.Handle(new ListActiveWorkModesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].Id.Should().Be(1);
        result.Value[0].Code.Should().Be("on_site");
        result.Value[0].Label.Should().Be("On-Site");
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoActiveWorkModesExist()
    {
        _repository
            .Setup(r => r.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.Handle(new ListActiveWorkModesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void Query_HasNoTenantIdProperty()
    {
        var properties = typeof(ListActiveWorkModesQuery)
            .GetProperties()
            .Select(p => p.Name);

        properties.Should().NotContain(name => string.Equals(name, "TenantId", StringComparison.OrdinalIgnoreCase));
    }
}
