using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class EmergencyContactCommandHandlerTests
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

        var handler = new AddEmergencyContactCommandHandler(commonRepo.Object, new Mock<IEmployeeProfileRepository>().Object, currentUser.Object);
        var result = await handler.Handle(new AddEmergencyContactCommand("Jane", "spouse", "555-1111", null, true), CancellationToken.None);

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

        var handler = new AddEmergencyContactCommandHandler(commonRepo.Object, profileRepo.Object, currentUser.Object);
        var result = await handler.Handle(new AddEmergencyContactCommand("Jane", "spouse", "555-1111", "jane@example.com", true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        profileRepo.Verify(r => r.AddEmergencyContactAsync(
            It.Is<ONEVO.Domain.Features.CoreHr.Entities.EmployeeEmergencyContact>(c => c.Name == "Jane" && c.EmployeeId == employeeId),
            It.IsAny<CancellationToken>()), Times.Once);
        profileRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenContactBelongsToDifferentEmployee()
    {
        var tenantId = Guid.NewGuid();
        var currentUser = AuthenticatedUser(tenantId, Guid.NewGuid());
        var employeeId = Guid.NewGuid();

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.GetEmergencyContactAsync(tenantId, employeeId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeEmergencyContact?)null);

        var handler = new UpdateEmergencyContactCommandHandler(commonRepo.Object, profileRepo.Object, currentUser.Object);
        var result = await handler.Handle(
            new UpdateEmergencyContactCommand(Guid.NewGuid(), "Jane", "spouse", "555-1111", null, true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenContactDoesNotExist()
    {
        var tenantId = Guid.NewGuid();
        var currentUser = AuthenticatedUser(tenantId, Guid.NewGuid());
        var employeeId = Guid.NewGuid();

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.GetEmergencyContactAsync(tenantId, employeeId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeEmergencyContact?)null);

        var handler = new DeleteEmergencyContactCommandHandler(commonRepo.Object, profileRepo.Object, currentUser.Object);
        var result = await handler.Handle(new DeleteEmergencyContactCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
