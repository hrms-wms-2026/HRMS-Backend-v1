using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.CreateLegalEntity;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class CreateLegalEntityCommandHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private CreateLegalEntityCommandHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _legalEntities.Setup(r => r.NameExistsForTenantAsync(TenantId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _legalEntities.Setup(r => r.CompanyCodeExistsForTenantAsync(TenantId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _legalEntities.Setup(r => r.RegistrationNumberExistsForTenantAsync(TenantId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return new CreateLegalEntityCommandHandler(_legalEntities.Object, _currentUser.Object);
    }

    private static CreateLegalEntityCommand ValidCommand(Guid? parentId = null) => new(
        "Acme Lanka",
        "ACME",
        "REG-001",
        "LKA",
        "LKR",
        null,
        null,
        parentId);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsSuccessWithSafeDefaults()
    {
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Acme Lanka");
        result.Value.CompanyCode.Should().Be("ACME");
        result.Value.Timezone.Should().Be("UTC");
        result.Value.FinancialYearStartMonth.Should().Be(1);
        result.Value.FirstDayOfWeek.Should().Be(1);
        result.Value.StandardWorkingDays.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 }, o => o.WithStrictOrdering());
        result.Value.DefaultLanguage.Should().Be("en-US");
        result.Value.DateFormat.Should().Be("DD MMM YYYY");
        result.Value.TimeFormat.Should().Be("12h");
        result.Value.Status.Should().Be("active");

        _legalEntities.Verify(r => r.AddAsync(
            It.Is<LegalEntityEntity>(e => e.TenantId == TenantId && e.IsPrimary == false && e.IsActive == true),
            It.IsAny<CancellationToken>()), Times.Once);
        _legalEntities.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsConflict_AndDoesNotPersist()
    {
        // BuildSut() registers a catch-all "no duplicates" setup; Moq resolves
        // ambiguous matches to the most-recently-registered setup, so the
        // specific override below must be added after BuildSut(), not before.
        var sut = BuildSut();
        _legalEntities.Setup(r => r.NameExistsForTenantAsync(TenantId, "Acme Lanka", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("already exists");
        _legalEntities.Verify(r => r.AddAsync(It.IsAny<LegalEntityEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateCompanyCode_ReturnsConflict_AndDoesNotPersist()
    {
        var sut = BuildSut();
        _legalEntities.Setup(r => r.CompanyCodeExistsForTenantAsync(TenantId, "ACME", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("code already exists");
        _legalEntities.Verify(r => r.AddAsync(It.IsAny<LegalEntityEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateRegistrationNumber_ReturnsConflict_AndDoesNotPersist()
    {
        var sut = BuildSut();
        _legalEntities.Setup(r => r.RegistrationNumberExistsForTenantAsync(TenantId, "REG-001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("registration number already exists");
        _legalEntities.Verify(r => r.AddAsync(It.IsAny<LegalEntityEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ParentNotFound_ReturnsNotFound_AndDoesNotPersist()
    {
        var parentId = Guid.NewGuid();
        _legalEntities.Setup(r => r.ParentExistsForTenantAsync(TenantId, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(parentId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Be("Parent company not found.");
        _legalEntities.Verify(r => r.AddAsync(It.IsAny<LegalEntityEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ParentBelongsToAnotherTenant_ReturnsNotFound_SameMessageAsMissing()
    {
        // ParentExistsForTenantAsync is tenant-scoped, so a parent belonging to
        // another tenant looks identical to a missing parent - never confirm
        // cross-tenant existence via a different message.
        var parentId = Guid.NewGuid();
        _legalEntities.Setup(r => r.ParentExistsForTenantAsync(TenantId, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(parentId), CancellationToken.None);

        result.Error.Should().Be("Parent company not found.");
    }

    [Fact]
    public async Task Handle_ValidParent_PersistsParentId()
    {
        var parentId = Guid.NewGuid();
        _legalEntities.Setup(r => r.ParentExistsForTenantAsync(TenantId, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = BuildSut();

        var result = await sut.Handle(ValidCommand(parentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _legalEntities.Verify(r => r.AddAsync(
            It.Is<LegalEntityEntity>(e => e.ParentLegalEntityId == parentId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);
        var sut = new CreateLegalEntityCommandHandler(_legalEntities.Object, _currentUser.Object);

        var result = await sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
