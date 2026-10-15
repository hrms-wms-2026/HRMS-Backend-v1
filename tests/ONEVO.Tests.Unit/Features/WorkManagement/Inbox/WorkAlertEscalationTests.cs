using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Inbox;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Tests.Unit.Features.Inbox;
using Xunit;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Inbox;

/// <summary>Plan W1: a Work monitor alert escalates up the Module chain (root = log only), by hand or after 3 days.</summary>
public sealed class WorkAlertEscalationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private readonly Guid _rootOwner = Guid.NewGuid(), _midOwner = Guid.NewGuid(), _leafOwner = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid(), _mid = Guid.NewGuid(), _leaf = Guid.NewGuid();
    private readonly Project _project = new() { Id = Guid.NewGuid(), Name = "Apollo" };
    private readonly MonitorAlert _alert;
    private readonly Mock<IMonitorAlertRepository> _alerts = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IInboxEscalationRecorder> _recorder = new();
    private readonly Mock<INotificationDispatcher> _notifications = new();
    private readonly InMemoryInboxDecisionJournal _journal = new();
    private readonly WmHierarchyPolicy _policy;

    public WorkAlertEscalationTests()
    {
        var task = Guid.NewGuid();
        _alert = new MonitorAlert
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = _project.Id, TargetType = MonitorTargetTypes.Task, TargetId = task,
            TargetTitle = "Checkout", RuleCode = "task_overdue", Message = "Past its due date", FirstDetectedAt = Now.AddDays(-4),
        };
        _alerts.Setup(a => a.GetByIdAsync(TenantId, _alert.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_alert);
        _alerts.Setup(a => a.ListOpenDetectedBeforeAsync(TenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([_alert]);
        var tree = new ProjectModuleTree(
        [
            new Objective { Id = _root, OwnerId = _rootOwner, IsDefault = true, Title = "Root" },
            new Objective { Id = _mid, ParentObjectiveId = _root, OwnerId = _midOwner, Title = "Payments" },
            new Objective { Id = _leaf, ParentObjectiveId = _mid, OwnerId = _leafOwner, Title = "Checkout" },
        ]);
        _hierarchy.Setup(h => h.LoadTreeAsync(TenantId, _project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tree);
        _hierarchy.Setup(h => h.FindActiveHolderAsync(TenantId, It.IsAny<ProjectModuleTree>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ProjectModuleTree t, Guid position, Guid _, CancellationToken _) => t.Get(position)?.OwnerId);
        var snapshots = new Mock<IProjectMonitorSnapshotLoader>();
        snapshots.Setup(s => s.LoadAsync(TenantId, _project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new ProjectMonitorSnapshot(
            _project.Id, WorkCalendar.Default, [], [],
            [new MonitorTask(task, _leaf, null, null, "Checkout", null, null, 0m, false, 0m, [])]));
        var projects = new Mock<IProjectRepository>();
        projects.Setup(p => p.GetByIdForTenantAsync(TenantId, _project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        var employees = new Mock<CoreEmployeeRepository>();
        employees.Setup(e => e.GetByIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => new Employee { Id = id, TenantId = TenantId, UserId = UserOf(id) });
        _policy = new WmHierarchyPolicy(_alerts.Object, snapshots.Object, _hierarchy.Object, projects.Object, employees.Object);
        // The recorder journals the escalation the way the real one does.
        _recorder.Setup(r => r.RecordAsync(TenantId, It.IsAny<EscalationRecord>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid tenant, EscalationRecord record, CancellationToken ct) =>
            {
                var targets = record.Target is EscalationTarget.Employees e ? e.Recipients.Select(r => r.EmployeeId!.Value).ToList() : [];
                await _journal.RecordAsync(new InboxDecisionJournalEntry
                {
                    Id = Guid.NewGuid(), TenantId = tenant, Source = record.Item.Source, Kind = record.Item.Kind, ItemId = record.Item.Id,
                    Action = InboxJournalActions.Escalate, ActorEmployeeId = record.ActorEmployeeId ?? Guid.Empty, DecidedAt = Now,
                    AfterJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        escalatedTo = targets, loggedOnly = (record.Target as EscalationTarget.LogOnly)?.Reason,
                    }),
                }, ct);
                return (IReadOnlyCollection<Guid>)record.PreviousHolderEmployeeIds.Concat(targets).ToList();
            });
    }

    private static Guid UserOf(Guid employeeId) => new(employeeId.ToByteArray().Reverse().ToArray());

    private EscalationContext Context(params Guid[] holders)
        => new(new ItemRef("work", "monitor_alert", _alert.Id), "monitor_alert", holders, null, null, _project.Id);

    private WorkAlertEscalation Escalation()
    {
        var registry = new Mock<IInboxEscalationPolicyRegistry>();
        registry.Setup(r => r.For("work", "monitor_alert")).Returns(_policy);
        return new WorkAlertEscalation(registry.Object, _recorder.Object, _journal, _notifications.Object);
    }

    private static IEnumerable<Guid> To(EscalationTarget target)
        => target is EscalationTarget.Employees e ? e.Recipients.Select(r => r.EmployeeId!.Value) : [];

    [Fact]
    public async Task The_alert_starts_with_its_creator_position_holder_and_climbs_one_Module_owner_at_a_time_to_a_logged_root()
    {
        _policy.Sla.Should().Be(TimeSpan.FromDays(3));
        To(await _policy.InitialAsync(TenantId, Context(), default)).Should().Equal(_leafOwner);
        To(await _policy.NextAsync(TenantId, Context(_leafOwner), default)).Should().Equal(_midOwner);
        To(await _policy.NextAsync(TenantId, Context(_midOwner), default)).Should().Equal(_rootOwner);
        (await _policy.NextAsync(TenantId, Context(_rootOwner), default)).Should().BeOfType<EscalationTarget.LogOnly>();
    }

    [Fact]
    public async Task Each_escalation_continues_from_whoever_holds_the_alert_now_and_tells_them()
    {
        var escalation = Escalation();

        await escalation.EscalateAsync(TenantId, _alert, _leafOwner, "Blocked", default);
        await escalation.EscalateAsync(TenantId, _alert, _midOwner, "Still blocked", default);

        (await escalation.CurrentHoldersAsync(TenantId, _alert, default)).Should().Equal(_rootOwner);
        _notifications.Verify(n => n.SendTemplatedAsync(TenantId, UserOf(_midOwner), WorkAlertEscalation.EscalatedTemplate,
            It.IsAny<IReadOnlyDictionary<string, string>>(), "inbox:work:monitor_alert", _alert.Id, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.SendTemplatedAsync(TenantId, UserOf(_rootOwner), WorkAlertEscalation.EscalatedTemplate,
            It.IsAny<IReadOnlyDictionary<string, string>>(), "inbox:work:monitor_alert", _alert.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_sweep_escalates_an_alert_left_for_three_days_and_stops_once_the_root_only_logged_it()
    {
        var sweep = new WorkMonitorAlertSweepSource(_alerts.Object, Escalation());

        (await sweep.EscalateOverdueAsync(TenantId, Now, default)).Should().Be(1);   // leaf -> mid
        (await sweep.EscalateOverdueAsync(TenantId, Now, default)).Should().Be(0, "escalated less than 3 days ago");
        (await sweep.EscalateOverdueAsync(TenantId, Now.AddDays(4), default)).Should().Be(1); // mid -> root
        (await sweep.EscalateOverdueAsync(TenantId, Now.AddDays(8), default)).Should().Be(1); // root: logged only
        (await sweep.EscalateOverdueAsync(TenantId, Now.AddDays(12), default)).Should().Be(0, "nothing above the root");
    }
}
