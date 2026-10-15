using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Inbox;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.Inbox;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.WorkManagement.Inbox;
using ONEVO.Tests.Unit.Fakes;
using Xunit;
using Employee = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.Inbox;

public class InboxEscalationPolicyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LegalEntity = Guid.NewGuid();

    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPermissionRepository> _permissions = new();

    // subject -> manager -> director -> (top)
    private readonly Employee _subject = Person();
    private readonly Employee _manager = Person();
    private readonly Employee _director = Person();

    public InboxEscalationPolicyTests()
    {
        foreach (var e in new[] { _subject, _manager, _director })
            _employees.Setup(r => r.GetByIdAsync(TenantId, e.Id, It.IsAny<CancellationToken>())).ReturnsAsync(e);
        ManagerOf(_subject, _manager);
        ManagerOf(_manager, _director);
        _authority.Setup(a => a.ResolveApproverAsync(It.Is<EmployeeApprovalRouteRequest>(r => r.SubjectEmployeeId == _director.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.NotFound("No approver."));
    }

    private static Employee Person() => new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), LegalEntityId = LegalEntity };

    private void ManagerOf(Employee employee, Employee manager)
        => _authority.Setup(a => a.ResolveApproverAsync(
                It.Is<EmployeeApprovalRouteRequest>(r => r.SubjectEmployeeId == employee.Id && r.LegalEntityId == LegalEntity
                    && r.RequiredPermission == ExceptionPermissions.ManagerReview && r.Purpose == EmployeeAuthorityPurpose.ExceptionAlertReview),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                manager.Id, manager.UserId, Guid.NewGuid(), ExceptionPermissions.ManagerReview, EmployeeAuthorityPurpose.ExceptionAlertReview,
                EmployeeApprovalRouteSource.ReportingLine, null)));

    private EscalationContext Context(string source, params Guid[] holders)
        => new(new ItemRef(source, "case", Guid.NewGuid()), "MeetingNoShow", holders, _subject.Id, LegalEntity, null);

    private ReportingLineThenHrPolicy Monitoring() => new(_ => _authority.Object, _employees.Object, _permissions.Object, new FakeDateTimeProvider());

    private ReportingLineThenLogPolicy Calendar() => new(_ => _authority.Object, _employees.Object);

    private static IReadOnlyList<Guid?> EmployeesOf(EscalationTarget target)
        => target.Should().BeOfType<EscalationTarget.Employees>().Subject.Recipients.Select(r => r.EmployeeId).ToList();

    [Fact]
    public async Task Monitoring_new_cases_go_to_the_reporting_manager()
        => EmployeesOf(await Monitoring().InitialAsync(TenantId, Context("monitoring"), default)).Should().Equal(_manager.Id);

    [Fact]
    public async Task Monitoring_falls_back_to_HR_when_nobody_above_can_review_and_never_alerts_the_subject()
    {
        _authority.Reset();
        _authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.NotFound("No approver."));
        var hr = Person();
        _permissions.Setup(p => p.ListUserIdsWithPermissionCodeAsync(TenantId, ExceptionPermissions.HrReview, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([hr.UserId, _subject.UserId]);
        _employees.Setup(r => r.GetDefaultForUserAsync(TenantId, hr.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(hr);

        var target = await Monitoring().InitialAsync(TenantId, Context("monitoring"), default);

        target.Should().BeOfType<EscalationTarget.Employees>().Subject.Recipients.Should().Equal(new EscalationRecipient(hr.UserId, hr.Id));
    }

    [Fact]
    public async Task Monitoring_escalation_always_goes_to_HR_and_is_log_only_when_there_is_no_HR()
    {
        _permissions.Setup(p => p.ListUserIdsWithPermissionCodeAsync(TenantId, ExceptionPermissions.HrReview, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var target = await Monitoring().NextAsync(TenantId, Context("monitoring", _manager.Id), default);

        target.Should().BeOfType<EscalationTarget.LogOnly>();
    }

    [Fact]
    public async Task Calendar_chain_is_manager_then_the_managers_manager_then_log_only_and_never_HR()
    {
        var policy = Calendar();

        EmployeesOf(await policy.InitialAsync(TenantId, Context("calendar"), default)).Should().Equal(_manager.Id);
        EmployeesOf(await policy.NextAsync(TenantId, Context("calendar", _manager.Id), default)).Should().Equal(_director.Id);
        (await policy.NextAsync(TenantId, Context("calendar", _director.Id), default))
            .Should().BeOfType<EscalationTarget.LogOnly>().Which.Reason.Should().Be(ReportingLineThenLogPolicy.NoOneAbove);
        _permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Calendar_never_escalates_back_to_someone_already_holding_the_case()
    {
        ManagerOf(_director, _manager);

        var target = await Calendar().NextAsync(TenantId, Context("calendar", _director.Id, _manager.Id), default);

        target.Should().BeOfType<EscalationTarget.LogOnly>();
    }

    // W1 replaced the log-only stub with the Module chain (WorkAlertEscalationTests); here: its SLA, and a gone alert is only logged.
    [Fact]
    public async Task Work_management_alerts_have_a_three_day_SLA_and_a_missing_alert_is_only_logged()
    {
        var policy = Wm();

        policy.Sla.Should().Be(TimeSpan.FromDays(3));
        (await policy.NextAsync(TenantId, Context("work"), default)).Should().BeOfType<EscalationTarget.LogOnly>();
    }

    private static WmHierarchyPolicy Wm() => new(
        Mock.Of<ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces.IMonitorAlertRepository>(),
        Mock.Of<ONEVO.Application.Features.WorkManagement.Monitoring.Services.IProjectMonitorSnapshotLoader>(),
        Mock.Of<ONEVO.Application.Features.WorkManagement.Hierarchy.IWorkHierarchyService>(),
        Mock.Of<ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces.IProjectRepository>(),
        Mock.Of<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>());

    [Theory]
    [InlineData("monitoring", "SustainedLowActivity", "reporting_line_then_hr")]
    [InlineData("monitoring", "IdentityAnomaly", "reporting_line_then_hr")]
    [InlineData("monitoring", "MeetingNoShow", null)]
    [InlineData("calendar", "MeetingNoShow", "reporting_line_then_log")]
    [InlineData("work", "monitor_alert", "wm_hierarchy")]
    [InlineData("leave", "request", null)]
    public void The_registry_picks_one_policy_per_source_and_type(string source, string type, string? expected)
    {
        var registry = new InboxEscalationPolicyRegistry([Monitoring(), Calendar(), Wm()]);

        registry.For(source, type)?.Name.Should().Be(expected);
        if (expected is null)
            registry.For(source, type).Should().BeNull();
    }
}
