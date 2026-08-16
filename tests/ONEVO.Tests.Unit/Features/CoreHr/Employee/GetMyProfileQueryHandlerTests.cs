using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
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
            new Mock<IEncryptionService>().Object,
            currentUser.Object);

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
