using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingOverview;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class ListOffboardingOverviewQueryHandlerTests
{
    private static EmployeeListItemResponse Item(Guid employeeId) => new(
        employeeId, "E-1", "Ada Lovelace", "ada@offboarding.onevo.dev",
        null, "Engineering", null, "Engineer", null, null, "Full-Time", "Active", null, null);

    [Fact]
    public async Task Handle_EmployeeWithNoOffboardingRecord_CanStartOffboardingIsTrue()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var scopeResolver = new Mock<IEmployeeVisibilityScopeResolver>();
        var employeeRepository = new Mock<IEmployeeRepository>();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(userId);
        scopeResolver.Setup(r => r.ResolveAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        employeeRepository.Setup(r => r.ListVisibleAsync(tenantId, It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<EmployeeListItemResponse> { Item(employeeId) }, 1));
        offboardingRecordRepository.Setup(r => r.GetLatestStatusesByEmployeeIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var result = await new ListOffboardingOverviewQueryHandler(scopeResolver.Object, employeeRepository.Object, offboardingRecordRepository.Object, currentUser.Object)
            .Handle(new ListOffboardingOverviewQuery(), CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value![0].CanStartOffboarding.Should().BeTrue();
        result.Value[0].CurrentOffboardingStatus.Should().BeNull();
        result.Value[0].EmployeeName.Should().Be("Ada Lovelace");
    }

    [Fact]
    public async Task Handle_EmployeeWithOpenOffboarding_CanStartOffboardingIsFalse()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var scopeResolver = new Mock<IEmployeeVisibilityScopeResolver>();
        var employeeRepository = new Mock<IEmployeeRepository>();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(userId);
        scopeResolver.Setup(r => r.ResolveAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        employeeRepository.Setup(r => r.ListVisibleAsync(tenantId, It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<EmployeeListItemResponse> { Item(employeeId) }, 1));
        offboardingRecordRepository.Setup(r => r.GetLatestStatusesByEmployeeIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [employeeId] = "in_progress" });

        var result = await new ListOffboardingOverviewQueryHandler(scopeResolver.Object, employeeRepository.Object, offboardingRecordRepository.Object, currentUser.Object)
            .Handle(new ListOffboardingOverviewQuery(), CancellationToken.None);

        result.Value![0].CanStartOffboarding.Should().BeFalse();
        result.Value[0].CurrentOffboardingStatus.Should().Be("in_progress");
    }

    [Fact]
    public async Task Handle_CallersOwnEmployeeRow_IsExcludedEvenThoughListVisibleAsyncIncludesIt()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ownEmployeeId = Guid.NewGuid();
        var coveredEmployeeId = Guid.NewGuid();
        var scopeResolver = new Mock<IEmployeeVisibilityScopeResolver>();
        var employeeRepository = new Mock<IEmployeeRepository>();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(userId);
        // OwnEmployeeId is set (as it always is once the caller has a profile) - ListVisibleAsync's
        // real predicate always includes the caller's own row regardless of coverage, so this is
        // what the resolver legitimately returns; the handler, not the repository, must filter it.
        scopeResolver.Setup(r => r.ResolveAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, ownEmployeeId, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        employeeRepository.Setup(r => r.ListVisibleAsync(tenantId, It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<EmployeeListItemResponse> { Item(ownEmployeeId), Item(coveredEmployeeId) }, 2));
        offboardingRecordRepository.Setup(r => r.GetLatestStatusesByEmployeeIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var result = await new ListOffboardingOverviewQueryHandler(scopeResolver.Object, employeeRepository.Object, offboardingRecordRepository.Object, currentUser.Object)
            .Handle(new ListOffboardingOverviewQuery(), CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value![0].EmployeeId.Should().Be(coveredEmployeeId);
    }
}
