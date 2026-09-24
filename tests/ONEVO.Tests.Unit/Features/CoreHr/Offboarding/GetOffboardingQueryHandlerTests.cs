using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.GetOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class GetOffboardingQueryHandlerTests
{
    [Fact]
    public async Task Handle_NoRecordExists_ReturnsSuccessWithNullValue()
    {
        var repo = new Mock<IOffboardingRecordRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        repo.Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OffboardingRecord?)null);

        var result = await new GetOffboardingQueryHandler(repo.Object, currentUser.Object)
            .Handle(new GetOffboardingQuery(employeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RecordExists_MapsToResponse()
    {
        var repo = new Mock<IOffboardingRecordRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        repo.Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OffboardingRecord
            {
                Id = Guid.NewGuid(), EmployeeId = employeeId, Reason = "resignation",
                LastWorkingDate = new DateOnly(2026, 12, 1), KnowledgeRiskLevel = "low",
                Status = OffboardingRecordStatuses.InProgress,
            });

        var result = await new GetOffboardingQueryHandler(repo.Object, currentUser.Object)
            .Handle(new GetOffboardingQuery(employeeId), CancellationToken.None);

        result.Value!.Status.Should().Be(OffboardingRecordStatuses.InProgress);
        result.Value.Reason.Should().Be("resignation");
    }
}
