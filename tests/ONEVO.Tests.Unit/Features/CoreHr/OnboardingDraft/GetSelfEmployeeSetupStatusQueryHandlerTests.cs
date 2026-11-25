using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.GetSelfEmployeeSetupStatus;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingDrafts;

public sealed class GetSelfEmployeeSetupStatusQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<IDepartmentRepository> _departments = new();

    public GetSelfEmployeeSetupStatusQueryHandlerTests()
    {
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, TenantId = TenantId, FirstName = "Ada", LastName = "Owner", Email = "ada@acme.test" });
        _legalEntities.Setup(l => l.CountActiveByTenantAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _positions.Setup(p => p.AnyActiveWithDepartmentAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _departments.Setup(d => d.AnyActiveAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private GetSelfEmployeeSetupStatusQueryHandler Handler()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        currentUser.SetupGet(c => c.UserId).Returns(UserId);
        return new GetSelfEmployeeSetupStatusQueryHandler(
            currentUser.Object, _employees.Object, _users.Object, _legalEntities.Object, _departments.Object, _positions.Object);
    }

    [Fact]
    public async Task HasProfile_CannotAddSelf()
    {
        _employees.Setup(e => e.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = Guid.NewGuid(), TenantId = TenantId });

        var result = await Handler().Handle(new GetSelfEmployeeSetupStatusQuery(), CancellationToken.None);

        Assert.True(result.Value!.HasEmployeeProfile);
        Assert.False(result.Value.CanAddSelf);
    }

    [Fact]
    public async Task NoLegalEntity_CannotAddSelf()
    {
        _legalEntities.Setup(l => l.CountActiveByTenantAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var result = await Handler().Handle(new GetSelfEmployeeSetupStatusQuery(), CancellationToken.None);

        Assert.False(result.Value!.HasLegalEntity);
        Assert.False(result.Value.CanAddSelf);
    }

    [Fact]
    public async Task NoDepartment_ReportsMissingDepartment()
    {
        _departments.Setup(d => d.AnyActiveAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Handler().Handle(new GetSelfEmployeeSetupStatusQuery(), CancellationToken.None);

        Assert.False(result.Value!.HasDepartment);
    }

    [Fact]
    public async Task Ready_ReportsDepartment()
    {
        var result = await Handler().Handle(new GetSelfEmployeeSetupStatusQuery(), CancellationToken.None);

        Assert.True(result.Value!.HasDepartment);
    }

    [Fact]
    public async Task NoPosition_CannotAddSelf()
    {
        _positions.Setup(p => p.AnyActiveWithDepartmentAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Handler().Handle(new GetSelfEmployeeSetupStatusQuery(), CancellationToken.None);

        Assert.False(result.Value!.HasPosition);
        Assert.False(result.Value.CanAddSelf);
    }

    [Fact]
    public async Task Ready_CanAddSelf_AndReturnsLoginNameAndEmail()
    {
        var result = await Handler().Handle(new GetSelfEmployeeSetupStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasEmployeeProfile);
        Assert.True(result.Value.CanAddSelf);
        Assert.Equal(("Ada", "Owner", "ada@acme.test"), (result.Value.FirstName, result.Value.LastName, result.Value.WorkEmail));
    }
}
