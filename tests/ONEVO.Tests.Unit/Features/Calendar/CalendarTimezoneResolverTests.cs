using Moq;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarTimezoneResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();

    private CalendarTimezoneResolver BuildSut() => new(_employees.Object, _legalEntities.Object);

    [Fact]
    public async Task ResolveForUserAsync_EmployeeHasDisplayTimezone_ReturnsIt()
    {
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, LegalEntityId = LegalEntityId, DisplayTimezone = "Asia/Colombo" });

        var result = await BuildSut().ResolveForUserAsync(TenantId, UserId, CancellationToken.None);

        Assert.Equal("Asia/Colombo", result);
        _legalEntities.Verify(x => x.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveForUserAsync_NoDisplayTimezone_FallsBackToLegalEntity()
    {
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, LegalEntityId = LegalEntityId, DisplayTimezone = null });
        _legalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = LegalEntityId, TenantId = TenantId, Timezone = "America/New_York" });

        var result = await BuildSut().ResolveForUserAsync(TenantId, UserId, CancellationToken.None);

        Assert.Equal("America/New_York", result);
    }

    [Fact]
    public async Task ResolveForUserAsync_NoLegalEntity_FallsBackToUtc()
    {
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, LegalEntityId = null, DisplayTimezone = null });

        var result = await BuildSut().ResolveForUserAsync(TenantId, UserId, CancellationToken.None);

        Assert.Equal("UTC", result);
    }

    [Fact]
    public async Task ResolveForUserAsync_NoEmployeeRecord_FallsBackToUtc()
    {
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var result = await BuildSut().ResolveForUserAsync(TenantId, UserId, CancellationToken.None);

        Assert.Equal("UTC", result);
    }
}
