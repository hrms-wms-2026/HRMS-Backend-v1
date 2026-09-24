using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddDependent;
using ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteDependent;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateDependent;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class DependentCommandHandlerTests
{
    private static Mock<ICurrentUser> AuthenticatedUser(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ICurrentUser>();
        mock.SetupGet(c => c.IsAuthenticated).Returns(true);
        mock.SetupGet(c => c.TenantId).Returns(tenantId);
        mock.SetupGet(c => c.UserId).Returns(userId);
        return mock;
    }

    [Fact]
    public async Task Add_ReturnsNotFound_WhenNoEmployeeRecordForCurrentUser()
    {
        var currentUser = AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid());
        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var handler = new AddDependentCommandHandler(commonRepo.Object, new Mock<IEmployeeProfileRepository>().Object, currentUser.Object);
        var result = await handler.Handle(
            new AddDependentCommand("Sam", "child", new DateOnly(2015, 1, 1), false, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Add_Succeeds_AndPersistsThroughProfileRepository()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var currentUser = AuthenticatedUser(tenantId, Guid.NewGuid());

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        var handler = new AddDependentCommandHandler(commonRepo.Object, profileRepo.Object, currentUser.Object);

        var result = await handler.Handle(
            new AddDependentCommand("Sam", "child", new DateOnly(2015, 1, 1), true, "555-2222"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        profileRepo.Verify(r => r.AddDependentAsync(
            It.Is<ONEVO.Domain.Features.CoreHr.Entities.EmployeeDependent>(d => d.Name == "Sam" && d.EmployeeId == employeeId && d.IsEmergencyContact),
            It.IsAny<CancellationToken>()), Times.Once);
        profileRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenDependentDoesNotBelongToCaller()
    {
        var tenantId = Guid.NewGuid();
        var currentUser = AuthenticatedUser(tenantId, Guid.NewGuid());
        var employeeId = Guid.NewGuid();

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.GetDependentAsync(tenantId, employeeId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeDependent?)null);

        var handler = new UpdateDependentCommandHandler(commonRepo.Object, profileRepo.Object, currentUser.Object);
        var result = await handler.Handle(
            new UpdateDependentCommand(Guid.NewGuid(), "Sam", "child", new DateOnly(2015, 1, 1), false, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenDependentDoesNotExist()
    {
        var tenantId = Guid.NewGuid();
        var currentUser = AuthenticatedUser(tenantId, Guid.NewGuid());
        var employeeId = Guid.NewGuid();

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.GetDependentAsync(tenantId, employeeId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeDependent?)null);

        var handler = new DeleteDependentCommandHandler(commonRepo.Object, profileRepo.Object, currentUser.Object);
        var result = await handler.Handle(new DeleteDependentCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
