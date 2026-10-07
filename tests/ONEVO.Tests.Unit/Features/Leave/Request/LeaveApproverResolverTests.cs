using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.Options;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveApproverResolverTests
{
    private static readonly DateOnly Today = new(2026, 8, 18);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();
    private readonly Mock<ILeaveRequestRepository> _requests = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public LeaveApproverResolverTests()
    {
        _clock.SetupGet(x => x.Today).Returns(Today);
        _clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero));
        _employees.Setup(x => x.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = _employeeId, TenantId = _tenantId, LegalEntityId = _legalEntityId });
        _authority.Setup(x => x.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.UnprocessableEntity("No eligible approver was found for this employee and action."));
        _requests.Setup(x => x.ListActiveDelegatesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _permissions.Setup(x => x.ListUserIdsWithPermissionCodeAsync(_tenantId, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _employees.Setup(x => x.GetByUserIdsAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private LeaveApproverResolver CreateResolver() => new(
        _authority.Object, _requests.Object, _permissions.Object, _employees.Object, _clock.Object,
        Options.Create(new LeaveRequestOptions()));

    private Task<LeaveApproverResolution> ResolveAsync() =>
        CreateResolver().ResolveAsync(_tenantId, _employeeId, Today, Today);

    private void SetAuthorityApprover(Guid approverEmployeeId) =>
        _authority.Setup(x => x.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                approverEmployeeId, Guid.NewGuid(), Guid.NewGuid(), LeaveApproverResolver.ApprovePermission,
                EmployeeAuthorityPurpose.TimeOffApproval, EmployeeApprovalRouteSource.ReportingLine, null)));

    /// <summary>Grants leave:manage and leave:approve to the given employees' users and makes
    /// them resolvable as employees in the tenant.</summary>
    private void SetHrEmployees(params Employee[] hr)
    {
        var userIds = hr.Select(e => e.UserId).ToList();
        _permissions.Setup(x => x.ListUserIdsWithPermissionCodeAsync(_tenantId, LeaveApproverResolver.HrFallbackPermission, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);
        _permissions.Setup(x => x.ListUserIdsWithPermissionCodeAsync(_tenantId, LeaveApproverResolver.ApprovePermission, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);
        _employees.Setup(x => x.GetByUserIdsAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) => hr.Where(e => ids.Contains(e.UserId)).ToList());
    }

    private Employee NewEmployee(string number, Guid? id = null, DateOnly? terminationDate = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = _tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = number,
        TerminationDate = terminationDate
    };

    [Fact]
    public async Task ResolveAsync_UsesEmployeeAuthorityRouteAsFirstApprover()
    {
        var approverId = Guid.NewGuid();
        SetAuthorityApprover(approverId);
        SetHrEmployees(NewEmployee("HR-001"));

        var result = await ResolveAsync();

        var row = result.Approvers.Should().ContainSingle().Subject;
        row.ApproverEmployeeId.Should().Be(approverId);
        row.SequenceOrder.Should().Be(1);
        row.DelegatedFromApproverId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_AsksAuthorityForTimeOffApprovalInTheEmployeesLegalEntity()
    {
        SetAuthorityApprover(Guid.NewGuid());

        await ResolveAsync();

        _authority.Verify(x => x.ResolveApproverAsync(
            It.Is<EmployeeApprovalRouteRequest>(r =>
                r.SubjectEmployeeId == _employeeId &&
                r.LegalEntityId == _legalEntityId &&
                r.RequiredPermission == "leave:approve" &&
                r.Purpose == EmployeeAuthorityPurpose.TimeOffApproval),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_AppliesActiveDelegation()
    {
        var approverId = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        SetAuthorityApprover(approverId);
        _requests.Setup(x => x.ListActiveDelegatesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveApprovalDelegateRow(approverId, delegateId)]);

        var result = await ResolveAsync();

        var row = result.Approvers.Should().ContainSingle().Subject;
        row.ApproverEmployeeId.Should().Be(delegateId);
        row.DelegatedFromApproverId.Should().Be(approverId);
    }

    [Fact]
    public async Task ResolveAsync_NoAuthorityRoute_RoutesToHrManager()
    {
        var hr = NewEmployee("HR-001");
        SetHrEmployees(hr);

        var result = await ResolveAsync();

        var row = result.Approvers.Should().ContainSingle().Subject;
        row.ApproverEmployeeId.Should().Be(hr.Id);
        row.SequenceOrder.Should().Be(1);
        row.DelegatedFromApproverId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_EmployeeWithoutLegalEntity_SkipsAuthorityAndRoutesToHrManager()
    {
        _employees.Setup(x => x.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = _employeeId, TenantId = _tenantId, LegalEntityId = null });
        var hr = NewEmployee("HR-001");
        SetHrEmployees(hr);

        var result = await ResolveAsync();

        result.Approvers.Should().ContainSingle().Which.ApproverEmployeeId.Should().Be(hr.Id);
        _authority.Verify(x => x.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_MultipleHrManagers_PicksOneByLowestEmployeeNumber()
    {
        // The policy's approval mode is read live, so several fallback rows under all_must_approve
        // would make every HR user approve. A single deterministic approver avoids that.
        var second = NewEmployee("DAPI-0030");
        var first = NewEmployee("DAPI-0025");
        SetHrEmployees(second, first);

        var result = await ResolveAsync();

        result.Approvers.Should().ContainSingle().Which.ApproverEmployeeId.Should().Be(first.Id);
    }

    [Fact]
    public async Task ResolveAsync_RequesterIsTheOnlyHrManager_ReturnsEmpty()
    {
        SetHrEmployees(NewEmployee("HR-001", id: _employeeId));

        var result = await ResolveAsync();

        result.Approvers.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_SkipsTerminatedHrManagers()
    {
        var terminated = NewEmployee("HR-001", terminationDate: Today.AddDays(-1));
        var active = NewEmployee("HR-002");
        SetHrEmployees(terminated, active);

        var result = await ResolveAsync();

        result.Approvers.Should().ContainSingle().Which.ApproverEmployeeId.Should().Be(active.Id);
    }

    [Fact]
    public async Task ResolveAsync_RequiresBothManageAndApprovePermissions()
    {
        // The approve endpoints are gated on leave:approve, so an HR user without it would be
        // assigned a request they get a 403 on.
        var manageOnly = NewEmployee("HR-001");
        var both = NewEmployee("HR-002");
        _permissions.Setup(x => x.ListUserIdsWithPermissionCodeAsync(_tenantId, LeaveApproverResolver.HrFallbackPermission, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([manageOnly.UserId, both.UserId]);
        _permissions.Setup(x => x.ListUserIdsWithPermissionCodeAsync(_tenantId, LeaveApproverResolver.ApprovePermission, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([both.UserId]);
        _employees.Setup(x => x.GetByUserIdsAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                new[] { manageOnly, both }.Where(e => ids.Contains(e.UserId)).ToList());

        var result = await ResolveAsync();

        result.Approvers.Should().ContainSingle().Which.ApproverEmployeeId.Should().Be(both.Id);
    }

    [Fact]
    public async Task ResolveAsync_HrFallbackAppliesActiveDelegation()
    {
        var hr = NewEmployee("HR-001");
        var delegateId = Guid.NewGuid();
        SetHrEmployees(hr);
        _requests.Setup(x => x.ListActiveDelegatesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveApprovalDelegateRow(hr.Id, delegateId)]);

        var result = await ResolveAsync();

        var row = result.Approvers.Should().ContainSingle().Subject;
        row.ApproverEmployeeId.Should().Be(delegateId);
        row.DelegatedFromApproverId.Should().Be(hr.Id);
    }

    [Fact]
    public async Task ResolveAsync_NoAuthorityRouteAndNoHrManager_ReturnsEmpty()
    {
        var result = await ResolveAsync();

        result.Approvers.Should().BeEmpty();
    }
}
