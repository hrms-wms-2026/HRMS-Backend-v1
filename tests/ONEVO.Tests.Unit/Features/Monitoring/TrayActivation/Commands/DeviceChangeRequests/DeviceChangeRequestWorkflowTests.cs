using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;
using ONEVO.Application.Features.Monitoring.TrayActivation.Queries.DeviceChangeRequests;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;

public sealed class DeviceChangeRequestWorkflowTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid ApproverUserId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApproveAsync_DeactivatesOldDeviceRevokesTokensAndMarksApproved()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest(currentDeviceRegistrationId: DeviceId);
        fixture.Requests.Setup(r => r.GetTrackedByIdAsync(TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await fixture.Workflow.ApproveAsync(
            new ApproveDeviceChangeRequestCommand(request.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(DeviceChangeRequest.StatusApproved);
        fixture.TrayActivation.Verify(t => t.DeactivateDeviceAsync(DeviceId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.TrayActivation.Verify(t => t.RevokeAllRefreshTokensForDeviceAsync(DeviceId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_WhenCallerIsNotTheResolvedApprover_ReturnsForbidden()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest(currentDeviceRegistrationId: DeviceId);
        fixture.Requests.Setup(r => r.GetTrackedByIdAsync(TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        fixture.Authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "attendance:approve",
                EmployeeAuthorityPurpose.DeviceChangeApproval, EmployeeApprovalRouteSource.PositionCoverage, 1)));

        var result = await fixture.Workflow.ApproveAsync(
            new ApproveDeviceChangeRequestCommand(request.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        request.Status.Should().NotBe(DeviceChangeRequest.StatusApproved);
    }

    [Fact]
    public async Task RejectAsync_RequiresReviewCommentAndLeavesDeviceUntouched()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest(currentDeviceRegistrationId: DeviceId);
        fixture.Requests.Setup(r => r.GetTrackedByIdAsync(TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await fixture.Workflow.RejectAsync(
            new RejectDeviceChangeRequestCommand(request.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        fixture.TrayActivation.Verify(t => t.DeactivateDeviceAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectAsync_WithComment_MarksRejected()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest(currentDeviceRegistrationId: DeviceId);
        fixture.Requests.Setup(r => r.GetTrackedByIdAsync(TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await fixture.Workflow.RejectAsync(
            new RejectDeviceChangeRequestCommand(request.Id, "Not their device"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(DeviceChangeRequest.StatusRejected);
        fixture.TrayActivation.Verify(t => t.DeactivateDeviceAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListApprovalsAsync_ReturnsPendingRequestForResolvedLegalEntity()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest(currentDeviceRegistrationId: DeviceId);
        fixture.Employees.Setup(e => e.GetDefaultForUserAsync(TenantId, ApproverUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId, FirstName = "Sam", LastName = "Approver" });
        fixture.Requests.Setup(r => r.ListPendingEmployeeIdsAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { EmployeeId });
        fixture.Authority.Setup(a => a.ResolveApprovalInboxScopeAsync(It.IsAny<EmployeeApprovalInboxScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { EmployeeId });
        fixture.Requests.Setup(r => r.ListApprovalInboxAsync(
                TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), 0, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { request }, 1));

        var result = await fixture.Workflow.ListApprovalsAsync(
            new ListDeviceChangeRequestApprovalsQuery(new PagedRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle(i => i.Id == request.Id);
    }

    [Fact]
    public async Task ApproveAsync_WhenLegalEntityIsUnresolved_ReturnsConflict()
    {
        var fixture = new Fixture(actingAsApprover: true);
        var request = fixture.PendingRequest(currentDeviceRegistrationId: DeviceId);
        request.LegalEntityId = null;
        fixture.Requests.Setup(r => r.GetTrackedByIdAsync(TenantId, request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await fixture.Workflow.ApproveAsync(
            new ApproveDeviceChangeRequestCommand(request.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        fixture.Authority.Verify(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture
    {
        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();
        public Mock<IEmployeeRepository> Employees { get; } = new();
        public Mock<IDeviceChangeRequestRepository> Requests { get; } = new();
        public Mock<ITrayActivationRepository> TrayActivation { get; } = new();
        public Mock<IEmployeeAuthorityResolver> Authority { get; } = new();
        public DeviceChangeRequestWorkflow Workflow { get; }

        public Fixture(bool actingAsApprover)
        {
            CurrentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
            CurrentUser.SetupGet(c => c.TenantId).Returns(TenantId);
            CurrentUser.SetupGet(c => c.UserId).Returns(actingAsApprover ? ApproverUserId : Guid.NewGuid());
            CurrentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns(true);
            Clock.SetupGet(c => c.UtcNow).Returns(Now);

            Employees.Setup(e => e.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, LegalEntityId = LegalEntityId, FirstName = "Alex", LastName = "Employee" });
            Employees.Setup(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, Employee>
                {
                    [EmployeeId] = new Employee { Id = EmployeeId, TenantId = TenantId, LegalEntityId = LegalEntityId, FirstName = "Alex", LastName = "Employee" },
                });

            Authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                    Guid.NewGuid(), ApproverUserId, Guid.NewGuid(), "attendance:approve",
                    EmployeeAuthorityPurpose.DeviceChangeApproval, EmployeeApprovalRouteSource.PositionCoverage, 1)));

            Requests.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            Workflow = new DeviceChangeRequestWorkflow(
                CurrentUser.Object, Clock.Object, Employees.Object, Requests.Object,
                TrayActivation.Object, Authority.Object);
        }

        public DeviceChangeRequest PendingRequest(Guid currentDeviceRegistrationId) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = EmployeeId,
            LegalEntityId = LegalEntityId,
            CurrentDeviceRegistrationId = currentDeviceRegistrationId,
            NewDeviceFingerprint = "fp-new",
            NewDeviceName = "New PC",
            NewDeviceOs = "Windows",
            Status = DeviceChangeRequest.StatusPending,
            RequestedAt = Now.AddMinutes(-10),
        };
    }
}
