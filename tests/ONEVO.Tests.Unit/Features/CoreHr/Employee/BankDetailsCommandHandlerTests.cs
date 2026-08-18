using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class BankDetailsCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsForbidden_WithoutEmployeesWritePermission()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(c => c.HasPermission("employees:write")).Returns(false);

        var handler = new UpdateBankDetailsCommandHandler(
            new Mock<CommonEmployeeRepo>().Object,
            new Mock<IEmployeeProfileRepository>().Object,
            new Mock<IEncryptionService>().Object,
            currentUser.Object);

        var result = await handler.Handle(
            new UpdateBankDetailsCommand("Test Bank", "Main", "Jane Doe", "1234567890", "savings", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_EncryptsAccountNumber_NeverStoresPlaintext()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(tenantId);
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(c => c.HasPermission("employees:write")).Returns(true);

        var commonRepo = new Mock<CommonEmployeeRepo>();
        commonRepo.Setup(r => r.GetByUserIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = employeeId, TenantId = tenantId });

        var profileRepo = new Mock<IEmployeeProfileRepository>();
        profileRepo.Setup(r => r.GetPrimaryBankDetailAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.EmployeeBankDetail?)null);

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Encrypt("1234567890")).Returns("ENCRYPTED_BLOB");

        ONEVO.Domain.Features.CoreHr.Entities.EmployeeBankDetail? saved = null;
        profileRepo.Setup(r => r.AddBankDetailAsync(It.IsAny<ONEVO.Domain.Features.CoreHr.Entities.EmployeeBankDetail>(), It.IsAny<CancellationToken>()))
            .Callback<ONEVO.Domain.Features.CoreHr.Entities.EmployeeBankDetail, CancellationToken>((b, _) => saved = b)
            .Returns(Task.CompletedTask);

        var handler = new UpdateBankDetailsCommandHandler(commonRepo.Object, profileRepo.Object, encryption.Object, currentUser.Object);

        var result = await handler.Handle(
            new UpdateBankDetailsCommand("Test Bank", "Main", "Jane Doe", "1234567890", "savings", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Equal("ENCRYPTED_BLOB", saved!.AccountNumberEncrypted);
        Assert.DoesNotContain("1234567890", saved.AccountNumberEncrypted);
    }
}
