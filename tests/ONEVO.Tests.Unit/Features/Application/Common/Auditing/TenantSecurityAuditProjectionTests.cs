using FluentAssertions;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.ReactivateUserAccount;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.RevokeUserAccountSession;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.SignOutUserAccount;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.SuspendUserAccount;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.ApproveBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;
using ONEVO.Application.Features.TimeAttendance.Commands.ArchiveClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.CreateClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.RestoreClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.UpdateClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Models;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Application.Common.Auditing;

/// <summary>
/// DEC-113 tenant security audit: one DescribeAudit test per audited tenant command. Each checks the
/// action name, the target, and that no name, free text, bank number or secret reaches the row.
/// </summary>
public class TenantSecurityAuditProjectionTests
{
    // Recognisable fake values: if any of them reaches an audit row, the projection leaks.
    private const string FakeName = "zzAuditLeakName";
    private const string FakeAccountNumber = "998877665544";
    private const string FakeRoutingNumber = "RT-FAKE-4471";
    private const string FakeText = "zzAuditLeakFreeText";

    private static readonly Guid UserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    private static string? DetailsJson(AuditEntry entry) => AuditJson.Serialize(entry.Details);

    private static void AssertNoLeak(AuditEntry entry)
    {
        var json = (DetailsJson(entry) ?? string.Empty) + entry.TargetId;
        foreach (var leak in new[] { FakeName, FakeAccountNumber, FakeRoutingNumber, FakeText })
            json.Should().NotContain(leak);
    }

    [Fact]
    public void SuspendUserAccount()
    {
        var entry = new SuspendUserAccountCommand(UserId).DescribeAudit(Result.Success());

        entry.Action.Should().Be("user.suspended");
        entry.TargetType.Should().Be("user");
        entry.TargetId.Should().Be(UserId.ToString());
        entry.Details.Should().BeNull();
    }

    [Fact]
    public void ReactivateUserAccount()
    {
        var entry = new ReactivateUserAccountCommand(UserId).DescribeAudit(Result.Success());

        entry.Action.Should().Be("user.reactivated");
        entry.TargetType.Should().Be("user");
        entry.TargetId.Should().Be(UserId.ToString());
        entry.Details.Should().BeNull();
    }

    [Fact]
    public void SignOutUserAccount_RecordsHowManySessionsEnded()
    {
        var entry = new SignOutUserAccountCommand(UserId)
            .DescribeAudit(Result<SignOutUserAccountResponse>.Success(new SignOutUserAccountResponse(3)));

        entry.Action.Should().Be("user.signed_out");
        entry.TargetType.Should().Be("user");
        entry.TargetId.Should().Be(UserId.ToString());
        DetailsJson(entry).Should().Be("{\"endedCount\":3}");
    }

    [Fact]
    public void RevokeUserAccountSession_RecordsOnlyTheSessionAndDeviceIds()
    {
        var entry = new RevokeUserAccountSessionCommand(UserId, OtherId, null).DescribeAudit(Result.Success());

        entry.Action.Should().Be("user.session_revoked");
        entry.TargetType.Should().Be("user");
        entry.TargetId.Should().Be(UserId.ToString());
        DetailsJson(entry).Should().Be($"{{\"sessionId\":\"{OtherId}\"}}");
    }

    [Fact]
    public void ApproveAccessGrantRequest_RecordsTheEmployeeIdOnly()
    {
        var response = new ApproveAccessGrantRequestResponse(
            OtherId, ThirdId, UserId, FakeText, InvitationQueued: true, ChecklistTaskCount: 4,
            PositionApprovalStatus: FakeText, MessageKey: FakeText);

        var entry = new ApproveAccessGrantRequestCommand(OtherId)
            .DescribeAudit(Result<ApproveAccessGrantRequestResponse>.Success(response));

        entry.Action.Should().Be("access_grant.approved");
        entry.TargetType.Should().Be("access_grant_request");
        entry.TargetId.Should().Be(OtherId.ToString());
        DetailsJson(entry).Should().Be($"{{\"employeeId\":\"{UserId}\"}}");
        AssertNoLeak(entry);
    }

    [Fact]
    public void UpdateBankDetails_RecordsTheFactNeverTheBankValues()
    {
        var command = new UpdateBankDetailsCommand(
            FakeName, FakeName, FakeName, FakeAccountNumber, "savings", FakeRoutingNumber);

        var entry = command.DescribeAudit(Result.Success());

        entry.Action.Should().Be("employee.bank_details_updated");
        entry.TargetType.Should().Be("employee");
        entry.Details.Should().BeNull();
        AssertNoLeak(entry);
    }

    [Fact]
    public void CompleteOffboarding()
    {
        var entry = new CompleteOffboardingCommand(OtherId).DescribeAudit(Result.Success());

        entry.Action.Should().Be("offboarding.completed");
        entry.TargetType.Should().Be("employee");
        entry.TargetId.Should().Be(OtherId.ToString());
        entry.Details.Should().BeNull();
    }

    [Fact]
    public void ApproveBypassRequest()
    {
        var entry = new ApproveBypassRequestCommand(OtherId).DescribeAudit(Result.Success());

        entry.Action.Should().Be("offboarding.bypass_approved");
        entry.TargetType.Should().Be("bypass_request");
        entry.TargetId.Should().Be(OtherId.ToString());
        entry.Details.Should().BeNull();
    }

    [Fact]
    public void UpdateMonitoringFeatureToggles_ValuesComeFromTheStateReader()
    {
        var command = new UpdateMonitoringFeatureTogglesCommand(
            true, true, false, false, true, false, false, true, false, false, false, 5, null);

        var entry = command.DescribeAudit(null);

        entry.Action.Should().Be("monitoring.toggles_updated");
        entry.TargetType.Should().Be("tenant");
        entry.TargetId.Should().BeNull();
        entry.Details.Should().BeNull();
    }

    private static ClockInPolicyResponse PolicyResponse() => new(
        ThirdId, OtherId, FakeName, new ClockInPolicyScopeResponse("company", [], [], []),
        new DateOnly(2026, 1, 1), null, false, FakeText, [], IsActive: true, UserId,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void CreateClockInPolicy_TargetsTheNewPolicyWithoutItsName()
    {
        var command = new CreateClockInPolicyCommand(
            OtherId, FakeName, new ClockInPolicyScopeInput("company", null, null, null),
            new DateOnly(2026, 1, 1), null, false, FakeText, [], IsActive: true);

        var entry = command.DescribeAudit(Result<ClockInPolicyResponse>.Success(PolicyResponse()));

        entry.Action.Should().Be("clock_in_policy.created");
        entry.TargetType.Should().Be("clock_in_policy");
        entry.TargetId.Should().Be(ThirdId.ToString());
        DetailsJson(entry).Should().Be($"{{\"legalEntityId\":\"{OtherId}\",\"isActive\":true}}");
        AssertNoLeak(entry);
    }

    [Fact]
    public void UpdateClockInPolicy_TargetsThePolicyWithoutItsName()
    {
        var command = new UpdateClockInPolicyCommand(
            OtherId, ThirdId, FakeName, new ClockInPolicyScopeInput("company", null, null, null),
            new DateOnly(2026, 1, 1), null, false, FakeText, [], IsActive: false);

        var entry = command.DescribeAudit(Result<ClockInPolicyResponse>.Success(PolicyResponse()));

        entry.Action.Should().Be("clock_in_policy.updated");
        entry.TargetType.Should().Be("clock_in_policy");
        entry.TargetId.Should().Be(ThirdId.ToString());
        DetailsJson(entry).Should().Be($"{{\"legalEntityId\":\"{OtherId}\",\"isActive\":false}}");
        AssertNoLeak(entry);
    }

    [Fact]
    public void ArchiveClockInPolicy()
    {
        var entry = new ArchiveClockInPolicyCommand(OtherId, ThirdId).DescribeAudit(Result<bool>.Success(true));

        entry.Action.Should().Be("clock_in_policy.archived");
        entry.TargetType.Should().Be("clock_in_policy");
        entry.TargetId.Should().Be(ThirdId.ToString());
        DetailsJson(entry).Should().Be($"{{\"legalEntityId\":\"{OtherId}\"}}");
    }

    [Fact]
    public void RestoreClockInPolicy()
    {
        var entry = new RestoreClockInPolicyCommand(OtherId, ThirdId).DescribeAudit(Result<bool>.Success(true));

        entry.Action.Should().Be("clock_in_policy.restored");
        entry.TargetType.Should().Be("clock_in_policy");
        entry.TargetId.Should().Be(ThirdId.ToString());
        DetailsJson(entry).Should().Be($"{{\"legalEntityId\":\"{OtherId}\"}}");
    }
}
