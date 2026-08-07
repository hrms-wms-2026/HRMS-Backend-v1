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
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ListLegalEntitiesQueryHandler BuildSut(bool hasUpdate = false, bool hasDelete = false)
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        _currentUser.Setup(c => c.HasPermission("legal_entity:update")).Returns(hasUpdate);
        _currentUser.Setup(c => c.HasPermission("legal_entity:delete")).Returns(hasDelete);
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
    public async Task Handle_ManagementAccess_ListsAllViaAccessibleQuery_WithHasManagementAccessTrue()
    {
        _legalEntities.Setup(r => r.ListAccessibleAsync(TenantId, UserId, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entity("Active Co", true)]);
        var sut = BuildSut(hasUpdate: true);

        var result = await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle().Which.Name.Should().Be("Active Co");
        _legalEntities.Verify(
            r => r.ListAccessibleAsync(TenantId, UserId, true, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DeletePermissionAlone_AlsoGrantsManagementAccess()
    {
        _legalEntities.Setup(r => r.ListAccessibleAsync(TenantId, UserId, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = BuildSut(hasDelete: true);

        await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        _legalEntities.Verify(
            r => r.ListAccessibleAsync(TenantId, UserId, true, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RegularUser_CallsAccessibleQuery_WithHasManagementAccessFalse()
    {
        _legalEntities.Setup(r => r.ListAccessibleAsync(TenantId, UserId, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entity("Own Co", true)]);
        var sut = BuildSut();

        var result = await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        result.Value!.Should().ContainSingle().Which.Name.Should().Be("Own Co");
        _legalEntities.Verify(
            r => r.ListAccessibleAsync(TenantId, UserId, false, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RegularUser_IncludeInactiveRequested_StillPassedThrough_ButFlaggedNonManagement()
    {
        // The repository is responsible for ignoring includeInactive on the non-management
        // branch; the handler's only job is to pass hasManagementAccess=false accurately.
        _legalEntities.Setup(r => r.ListAccessibleAsync(TenantId, UserId, false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = BuildSut();

        var result = await sut.Handle(new ListLegalEntitiesQuery(IncludeInactive: true), CancellationToken.None);

        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RegularUserWithNoEmployee_ReturnsEmptyList()
    {
        _legalEntities.Setup(r => r.ListAccessibleAsync(TenantId, UserId, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = BuildSut();

        var result = await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
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
