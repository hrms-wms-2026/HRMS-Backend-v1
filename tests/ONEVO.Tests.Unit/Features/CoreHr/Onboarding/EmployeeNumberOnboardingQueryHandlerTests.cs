using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.CheckEmployeeNumberAvailability;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetEmployeeNumberSuggestion;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class EmployeeNumberSuggestionQueryHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public EmployeeNumberSuggestionQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
    }

    private GetEmployeeNumberSuggestionQueryHandler CreateHandler()
        => new(_legalEntities.Object, _employees.Object, _currentUser.Object);

    [Fact]
    public async Task Suggest_UsesLegalEntityCompanyCodePrefix_AndNextSequence()
    {
        _legalEntities
            .Setup(r => r.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity
            {
                Id = _legalEntityId,
                TenantId = _tenantId,
                IsActive = true,
                CompanyCode = "DAPI"
            });
        _employees
            .Setup(r => r.GetNextEmployeeNumberSequenceAsync(_tenantId, "DAPI", It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _employees
            .Setup(r => r.EmployeeNumberExistsAsync(_tenantId, "DAPI-0005", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new GetEmployeeNumberSuggestionQuery(_legalEntityId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("DAPI-0005", result.Value!.EmployeeNumber);
        Assert.Equal("DAPI", result.Value.Prefix);
        Assert.Equal(5, result.Value.Sequence);
    }

    [Fact]
    public async Task Suggest_SkipsExistingCollision()
    {
        _legalEntities
            .Setup(r => r.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity
            {
                Id = _legalEntityId,
                TenantId = _tenantId,
                IsActive = true,
                CompanyCode = "DAPI"
            });
        _employees
            .Setup(r => r.GetNextEmployeeNumberSequenceAsync(_tenantId, "DAPI", It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _employees
            .Setup(r => r.EmployeeNumberExistsAsync(_tenantId, "DAPI-0005", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _employees
            .Setup(r => r.EmployeeNumberExistsAsync(_tenantId, "DAPI-0006", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new GetEmployeeNumberSuggestionQuery(_legalEntityId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("DAPI-0006", result.Value!.EmployeeNumber);
        Assert.Equal(6, result.Value.Sequence);
    }

    [Fact]
    public async Task Suggest_ReturnsNotFound_ForOtherTenantLegalEntity()
    {
        _legalEntities
            .Setup(r => r.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntity?)null);

        var result = await CreateHandler().Handle(new GetEmployeeNumberSuggestionQuery(_legalEntityId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Suggest_RejectsInactiveLegalEntity()
    {
        _legalEntities
            .Setup(r => r.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity
            {
                Id = _legalEntityId,
                TenantId = _tenantId,
                IsActive = false,
                CompanyCode = "DAPI"
            });

        var result = await CreateHandler().Handle(new GetEmployeeNumberSuggestionQuery(_legalEntityId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }
}

public sealed class EmployeeNumberAvailabilityQueryHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public EmployeeNumberAvailabilityQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Availability_ReturnsUnavailable_WhenExists()
    {
        _employees
            .Setup(r => r.EmployeeNumberExistsAsync(_tenantId, "DAPI-0005", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await new CheckEmployeeNumberAvailabilityQueryHandler(_employees.Object, _currentUser.Object)
            .Handle(new CheckEmployeeNumberAvailabilityQuery("DAPI-0005"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Available);
        Assert.Equal("DAPI-0005", result.Value.EmployeeNumber);
    }

    [Fact]
    public async Task Availability_RejectsInvalidFormat()
    {
        var result = await new CheckEmployeeNumberAvailabilityQueryHandler(_employees.Object, _currentUser.Object)
            .Handle(new CheckEmployeeNumberAvailabilityQuery("DAPI 0005"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("letters, numbers, hyphens, and underscores", result.Error!);
    }

    [Fact]
    public async Task Availability_RejectsBlank()
    {
        var result = await new CheckEmployeeNumberAvailabilityQueryHandler(_employees.Object, _currentUser.Object)
            .Handle(new CheckEmployeeNumberAvailabilityQuery("   "), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
