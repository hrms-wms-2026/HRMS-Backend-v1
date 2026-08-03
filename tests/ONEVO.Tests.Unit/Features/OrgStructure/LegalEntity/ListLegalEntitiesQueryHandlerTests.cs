using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.ListLegalEntities;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class ListLegalEntitiesQueryHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private ListLegalEntitiesQueryHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        return new ListLegalEntitiesQueryHandler(_legalEntities.Object, _currentUser.Object);
    }

    private static LegalEntityEntity Entity(string name, bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        Name = name,
        CountryCode = "LKA",
        CurrencyCode = "LKR",
        IsActive = isActive
    };

    [Fact]
    public async Task Handle_DefaultView_ExcludesInactiveCompanies()
    {
        _legalEntities.Setup(r => r.ListByTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entity("Active Co", true), Entity("Inactive Co", false)]);
        var sut = BuildSut();

        var result = await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle().Which.Name.Should().Be("Active Co");
    }

    [Fact]
    public async Task Handle_IncludeInactiveTrue_ReturnsAllCompanies()
    {
        _legalEntities.Setup(r => r.ListByTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entity("Active Co", true), Entity("Inactive Co", false)]);
        var sut = BuildSut();

        var result = await sut.Handle(new ListLegalEntitiesQuery(IncludeInactive: true), CancellationToken.None);

        result.Value!.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);
        var sut = new ListLegalEntitiesQueryHandler(_legalEntities.Object, _currentUser.Object);

        var result = await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
