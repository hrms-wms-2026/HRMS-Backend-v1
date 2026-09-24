using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using Xunit;
using FeatureEmployeeRepo = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class UpdatePersonalInformationCommandHandlerTests
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

        var handler = new UpdatePersonalInformationCommandHandler(
            commonRepo.Object,
            new Mock<FeatureEmployeeRepo>().Object,
            new Mock<IEmployeeProfileRepository>().Object,
            currentUser.Object);

        var result = await handler.Handle(
            new UpdatePersonalInformationCommand("Jane", "Doe", null, null, null, null, null, [], "1"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UpdatesTrackedEmployeeAndReplacesAddresses()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(tenantId);
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());

        var lookupEmployee = new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId };
        var trackedEmployee = new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId, FirstName = "Old" };

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lookupEmployee);

        var featureRepo = new Mock<FeatureEmployeeRepo>();
        featureRepo.Setup(r => r.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackedEmployee);

        var profileRepo = new Mock<IEmployeeProfileRepository>();

        var handler = new UpdatePersonalInformationCommandHandler(
            commonRepo.Object, featureRepo.Object, profileRepo.Object, currentUser.Object);

        var result = await handler.Handle(
            new UpdatePersonalInformationCommand("Jane", "Doe", "555-1111", null, null, null, "Asia/Colombo", [], "1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane", trackedEmployee.FirstName);
        Assert.Equal("Doe", trackedEmployee.LastName);
        Assert.Equal("Asia/Colombo", trackedEmployee.DisplayTimezone);
        profileRepo.Verify(r => r.ReplaceAddresses(tenantId, employeeId, It.IsAny<IReadOnlyList<ONEVO.Domain.Features.CoreHr.Entities.EmployeeAddress>>()), Times.Once);
        profileRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
