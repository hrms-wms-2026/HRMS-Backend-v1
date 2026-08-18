using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;
using FeatureEmployeeRepo = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class GetMyProfileQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsNotFound_WhenNoEmployeeRecordForCurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var handler = new GetMyProfileQueryHandler(
            commonRepo.Object,
            new Mock<FeatureEmployeeRepo>().Object,
            new Mock<IEmployeeProfileRepository>().Object,
            new Mock<IWorkModeRepository>().Object,
            new Mock<IUserRepository>().Object,
            new Mock<IUserMfaRepository>().Object,
            new Mock<IEncryptionService>().Object,
            new Mock<ILegalEntityRepository>().Object,
            currentUser.Object);

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReportsMfaEnabledTrue_WhenAVerifiedTotpRegistrationExists()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(tenantId);
        currentUser.SetupGet(c => c.UserId).Returns(userId);

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee
            {
                Id = employeeId, TenantId = tenantId, UserId = userId,
                FirstName = "Jane", LastName = "Doe", Email = "jane@example.com",
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow), EmployeeNumber = "E-001"
            });

        var featureRepo = new Mock<FeatureEmployeeRepo>();
        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(w => w.ListActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.ListAddressesAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        profileRepo.Setup(r => r.ListEmergencyContactsAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        profileRepo.Setup(r => r.ListDependentsAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        profileRepo.Setup(r => r.GetPrimaryBankDetailAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeBankDetail?)null);

        var lastUpdated = DateTimeOffset.UtcNow;
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, UpdatedAt = lastUpdated });

        var userMfa = new Mock<IUserMfaRepository>();
        userMfa.Setup(m => m.GetTotpAsync(userId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserMfa { Id = Guid.NewGuid(), UserId = userId, MethodType = "totp", IsVerified = true });

        var handler = new GetMyProfileQueryHandler(
            commonRepo.Object, featureRepo.Object, profileRepo.Object, workModes.Object,
            users.Object, userMfa.Object, new Mock<IEncryptionService>().Object,
            new Mock<ILegalEntityRepository>().Object, currentUser.Object);

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Security.MfaEnabled);
        Assert.Equal(lastUpdated, result.Value.Security.LastPasswordChangedAt);
    }
}
