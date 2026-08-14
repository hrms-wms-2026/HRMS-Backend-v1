using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateLegalEntityGeneralSettings;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class UpdateLegalEntityGeneralSettingsCommandHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private UpdateLegalEntityGeneralSettingsCommandHandler BuildSut(bool hasManagementAccess = true)
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        _currentUser.Setup(c => c.HasPermission("legal_entity:update")).Returns(hasManagementAccess);
        _currentUser.Setup(c => c.HasPermission("legal_entity:delete")).Returns(false);
        _dateTimeProvider.SetupGet(d => d.UtcNow).Returns(FixedNow);
        return new UpdateLegalEntityGeneralSettingsCommandHandler(
            _legalEntities.Object, _currentUser.Object, _dateTimeProvider.Object);
    }

    private static LegalEntityEntity ExistingEntity(Guid id) => new()
    {
        Id = id,
        TenantId = TenantId,
        Name = "Old Name",
        CompanyCode = "OLD",
        RegistrationNumber = "REG-OLD",
        CountryCode = "LKA",
        CurrencyCode = "LKR",
        IsActive = true,
        IsPrimary = true,
        LogoFileId = Guid.NewGuid(),
        ParentLegalEntityId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
    };

    private static UpdateLegalEntityGeneralSettingsCommand ValidCommand(
        Guid id, string status = "active", TimeOnly? workStartTime = null, TimeOnly? workEndTime = null) => new(
        id,
        "New Name",
        "NEW",
        "REG-NEW",
        null, null, null, null, null,
        "LKA",
        "LKR",
        "Asia/Colombo",
        1,
        1,
        [3, 1, 2],
        "en-US",
        "DD MMM YYYY",
        "12h",
        status,
        workStartTime,
        workEndTime);

    private void SetupNoDuplicates(Guid excludeId)
    {
        _legalEntities.Setup(r => r.NameExistsForTenantAsync(TenantId, It.IsAny<string>(), excludeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _legalEntities.Setup(r => r.CompanyCodeExistsForTenantAsync(TenantId, It.IsAny<string>(), excludeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _legalEntities.Setup(r => r.RegistrationNumberExistsForTenantAsync(TenantId, It.IsAny<string>(), excludeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [Fact]
    public async Task Handle_ValidRequest_FetchesExisting_MutatesAndPersists()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        SetupNoDuplicates(entity.Id);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("New Name");
        result.Value.CompanyCode.Should().Be("NEW");
        result.Value.StandardWorkingDays.Should().BeEquivalentTo([1, 2, 3], o => o.WithStrictOrdering());

        _legalEntities.Verify(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()), Times.Once);
        _legalEntities.Verify(r => r.Update(entity), Times.Once);
        _legalEntities.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsUpdatedAt_FromDateTimeProvider_NotSystemClock()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        SetupNoDuplicates(entity.Id);
        var sut = BuildSut();

        await sut.Handle(ValidCommand(entity.Id), CancellationToken.None);

        entity.UpdatedAt.Should().Be(FixedNow);
        _dateTimeProvider.VerifyGet(d => d.UtcNow, Times.AtLeastOnce);
    }

    [Fact]
    public async Task Handle_ValidRequest_PreservesFieldsNotExposedInRequest()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        var originalLogoFileId = entity.LogoFileId;
        var originalParentId = entity.ParentLegalEntityId;
        var originalCreatedAt = entity.CreatedAt;
        var originalIsPrimary = entity.IsPrimary;
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        SetupNoDuplicates(entity.Id);
        var sut = BuildSut();

        await sut.Handle(ValidCommand(entity.Id), CancellationToken.None);

        entity.LogoFileId.Should().Be(originalLogoFileId);
        entity.ParentLegalEntityId.Should().Be(originalParentId);
        entity.CreatedAt.Should().Be(originalCreatedAt);
        entity.IsPrimary.Should().Be(originalIsPrimary);
        entity.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsWorkStartAndEndTime()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        SetupNoDuplicates(entity.Id);
        var sut = BuildSut();

        var result = await sut.Handle(
            ValidCommand(entity.Id, workStartTime: new TimeOnly(9, 0), workEndTime: new TimeOnly(17, 30)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.WorkStartTime.Should().Be(new TimeOnly(9, 0));
        entity.WorkEndTime.Should().Be(new TimeOnly(17, 30));
        result.Value!.WorkStartTime.Should().Be(new TimeOnly(9, 0));
        result.Value.WorkEndTime.Should().Be(new TimeOnly(17, 30));
    }

    [Fact]
    public async Task Handle_ValidRequest_NullWorkTimes_PersistsNull()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        entity.WorkStartTime = new TimeOnly(9, 0);
        entity.WorkEndTime = new TimeOnly(17, 0);
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        SetupNoDuplicates(entity.Id);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.WorkStartTime.Should().BeNull();
        entity.WorkEndTime.Should().BeNull();
    }

    [Fact]
    public async Task Handle_EntityNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_DuplicateNameExcludingSelf_ReturnsConflict()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _legalEntities.Setup(r => r.NameExistsForTenantAsync(TenantId, "New Name", entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _legalEntities.Verify(r => r.Update(It.IsAny<LegalEntityEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateCompanyCodeExcludingSelf_ReturnsConflict()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _legalEntities.Setup(r => r.NameExistsForTenantAsync(TenantId, It.IsAny<string>(), entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _legalEntities.Setup(r => r.CompanyCodeExistsForTenantAsync(TenantId, "NEW", entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_DuplicateRegistrationNumberExcludingSelf_ReturnsConflict()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _legalEntities.Setup(r => r.NameExistsForTenantAsync(TenantId, It.IsAny<string>(), entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _legalEntities.Setup(r => r.CompanyCodeExistsForTenantAsync(TenantId, It.IsAny<string>(), entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _legalEntities.Setup(r => r.RegistrationNumberExistsForTenantAsync(TenantId, "REG-NEW", entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_DeactivatingLastActiveCompany_ReturnsFailure_AndDoesNotPersist()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        entity.IsActive = true;
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        SetupNoDuplicates(entity.Id);
        _legalEntities.Setup(r => r.CountActiveByTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(entity.Id, status: "inactive"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("last active company");
        _legalEntities.Verify(r => r.Update(It.IsAny<LegalEntityEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DeactivatingWhenOtherActiveCompaniesExist_Succeeds()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        entity.IsActive = true;
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        SetupNoDuplicates(entity.Id);
        _legalEntities.Setup(r => r.CountActiveByTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(entity.Id, status: "inactive"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_RegularUser_OwnAccessibleCompany_UpdatesSuccessfully()
    {
        var entity = ExistingEntity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, entity.Id, UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        SetupNoDuplicates(entity.Id);
        var sut = BuildSut(hasManagementAccess: false);

        var result = await sut.Handle(ValidCommand(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RegularUser_AnotherCompanyInSameTenant_ReturnsNotFound_AndDoesNotUpdate()
    {
        // Same isolation caveat as the Get handler's equivalent test - this caller can
        // never reach UpdateGeneralSettings over HTTP without legal_entity:update, which
        // already implies management access under LegalEntityAccessPolicy. This proves
        // the handler's own defense-in-depth filter, not an HTTP-reachable path.
        var otherCompanyId = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetAccessibleByIdAsync(TenantId, otherCompanyId, UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = BuildSut(hasManagementAccess: false);

        var result = await sut.Handle(ValidCommand(otherCompanyId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _legalEntities.Verify(r => r.Update(It.IsAny<LegalEntityEntity>()), Times.Never);
    }
}
