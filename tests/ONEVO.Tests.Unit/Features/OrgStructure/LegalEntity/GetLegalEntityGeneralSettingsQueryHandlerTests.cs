using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityGeneralSettings;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class GetLegalEntityGeneralSettingsQueryHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private GetLegalEntityGeneralSettingsQueryHandler BuildSut(bool hasManagementAccess = true)
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        _currentUser.Setup(c => c.HasPermission("legal_entity:update")).Returns(hasManagementAccess);
        _currentUser.Setup(c => c.HasPermission("legal_entity:delete")).Returns(false);
        return new GetLegalEntityGeneralSettingsQueryHandler(_legalEntities.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_EntityInTenant_ReturnsMappedResponse()
    {
        var entity = new LegalEntityEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Name = "Acme Lanka",
            CountryCode = "LKA",
            CurrencyCode = "LKR",
            IsActive = true
        };
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var sut = BuildSut();

        var result = await sut.Handle(new GetLegalEntityGeneralSettingsQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(entity.Id);
        result.Value.Status.Should().Be("active");
    }

    [Fact]
    public async Task Handle_EntityBelongsToAnotherTenant_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = BuildSut();

        var result = await sut.Handle(new GetLegalEntityGeneralSettingsQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_RegularUser_OwnAccessibleCompany_ReturnsMappedResponse()
    {
        var entity = new LegalEntityEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Name = "Own Co",
            CountryCode = "LKA",
            CurrencyCode = "LKR",
            IsActive = true
        };
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var sut = BuildSut(hasManagementAccess: false);

        var result = await sut.Handle(new GetLegalEntityGeneralSettingsQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(entity.Id);
    }

    [Fact]
    public async Task Handle_RegularUser_AnotherCompanyInSameTenant_ReturnsNotFound()
    {
        // Exercises the handler's own accessible-company filter in isolation. This exact
        // caller (no legal_entity:update) can never reach this handler over HTTP -
        // LegalEntitiesController.GetGeneralSettings requires legal_entity:update, which
        // under LegalEntityAccessPolicy.HasManagementAccess already implies management
        // access. This unit test is the only real coverage of the filter's non-management
        // branch; see LEGAL_ENTITY_CREATE_ACCESS_REPORT.md for why.
        var otherCompanyId = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, otherCompanyId, UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = BuildSut(hasManagementAccess: false);

        var result = await sut.Handle(new GetLegalEntityGeneralSettingsQuery(otherCompanyId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
