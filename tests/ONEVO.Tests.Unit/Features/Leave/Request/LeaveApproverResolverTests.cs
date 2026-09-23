using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Options;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveApproverResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesDirectManagerAsFirstApprover()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var hierarchy = new Mock<IEmployeeHierarchyClosureRepository>();
        hierarchy.Setup(x => x.GetDirectManagerEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerId);
        var requests = new Mock<ILeaveRequestRepository>();
        requests.Setup(x => x.ListActiveDelegatesAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var resolver = new LeaveApproverResolver(hierarchy.Object, requests.Object, Options.Create(new LeaveRequestOptions()));
        var result = await resolver.ResolveAsync(tenantId, employeeId, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 18));

        result.Approvers.Should().ContainSingle().Which.ApproverEmployeeId.Should().Be(managerId);
    }

    [Fact]
    public async Task ResolveAsync_AppliesActiveDelegation()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        var hierarchy = new Mock<IEmployeeHierarchyClosureRepository>();
        hierarchy.Setup(x => x.GetDirectManagerEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerId);
        var requests = new Mock<ILeaveRequestRepository>();
        requests.Setup(x => x.ListActiveDelegatesAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveApprovalDelegateRow(managerId, delegateId)]);

        var resolver = new LeaveApproverResolver(hierarchy.Object, requests.Object, Options.Create(new LeaveRequestOptions()));
        var result = await resolver.ResolveAsync(tenantId, employeeId, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 20));

        var row = result.Approvers.Should().ContainSingle().Subject;
        row.ApproverEmployeeId.Should().Be(delegateId);
        row.DelegatedFromApproverId.Should().Be(managerId);
    }

    [Fact]
    public async Task ResolveAsync_MissingManager_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var hierarchy = new Mock<IEmployeeHierarchyClosureRepository>();
        hierarchy.Setup(x => x.GetDirectManagerEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        var requests = new Mock<ILeaveRequestRepository>();

        var resolver = new LeaveApproverResolver(hierarchy.Object, requests.Object, Options.Create(new LeaveRequestOptions()));
        var result = await resolver.ResolveAsync(tenantId, employeeId, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 18));

        result.Approvers.Should().BeEmpty();
    }
}
