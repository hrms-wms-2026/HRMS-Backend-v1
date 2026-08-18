using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.SetMyAvatar;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using Xunit;
using FeatureEmployeeRepo = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class SetMyAvatarCommandHandlerTests
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

        var handler = new SetMyAvatarCommandHandler(
            commonRepo.Object,
            new Mock<FeatureEmployeeRepo>().Object,
            new Mock<IFileStorageService>().Object,
            currentUser.Object);

        using var stream = new MemoryStream();
        var result = await handler.Handle(new SetMyAvatarCommand("a.png", "image/png", stream), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
