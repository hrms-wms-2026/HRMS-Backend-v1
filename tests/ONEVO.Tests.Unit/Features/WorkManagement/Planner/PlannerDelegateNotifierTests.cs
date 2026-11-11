using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Delegate task planning (2026-11-11), "Notifications": the principal is told, in-app, about every block a
/// delegate writes, with the time in the principal's planner zone.</summary>
public sealed class PlannerDelegateNotifierTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 45, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(1);

    private readonly Employee _ceo = Person("Chief", "Executive");
    private readonly Employee _secretary = Person("Sara", "Fernando");
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarTimezoneResolver> _timezones = new();
    private readonly List<WorkNotificationPayload> _sent = [];

    private static Employee Person(string first, string last) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = first, LastName = last,
        EmployeeNumber = "E1", HireDate = new DateOnly(2024, 1, 1), EmploymentStatusId = EmploymentStatusIds.Active,
    };

    public PlannerDelegateNotifierTests()
    {
        _employees.Setup(e => e.GetByIdAsync(Tenant, _ceo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_ceo);
        _employees.Setup(e => e.GetByIdAsync(Tenant, _secretary.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_secretary);
        _timezones.Setup(t => t.ResolveForUserAsync(Tenant, _ceo.UserId, It.IsAny<CancellationToken>())).ReturnsAsync("Asia/Colombo");
        _outbox.Setup(o => o.EnqueueAsync(OutboxMessageTypes.WorkNotification, It.IsAny<WorkNotificationPayload>(), Tenant, It.IsAny<CancellationToken>()))
            .Callback((string _, WorkNotificationPayload p, Guid? _, CancellationToken _) => _sent.Add(p))
            .Returns(Task.CompletedTask);
    }

    private PlannerDelegateNotifier Sut() => new(_outbox.Object, _employees.Object, _timezones.Object);

    [Theory]
    [InlineData(PlannerDelegateActions.Planned, "planner_block_planned_for_you")]
    [InlineData(PlannerDelegateActions.Moved, "planner_block_moved_for_you")]
    [InlineData(PlannerDelegateActions.Removed, "planner_block_removed_for_you")]
    public async Task BlockWritten_SendsOneNoticeToThePrincipal_WithWhenInTheirZone(string action, string code)
    {
        var blockId = Guid.NewGuid();

        await Sut().BlockWrittenAsync(Tenant, _ceo.Id, _secretary.Id, action, blockId, "Board pack", Start, End, CancellationToken.None);

        var notice = Assert.Single(_sent);
        Assert.Equal((Tenant, _ceo.UserId, code, "planned_task_block", (Guid?)blockId),
            (notice.TenantId, notice.RecipientUserId, notice.TemplateCode, notice.RelatedEntityType, notice.RelatedEntityId));
        Assert.Equal("Sara Fernando", notice.Placeholders["delegateName"]);
        Assert.Equal("Board pack", notice.Placeholders["taskTitle"]);
        Assert.Equal("Tuesday, November 10, 3:15 PM – 4:15 PM (Asia/Colombo)", notice.Placeholders["when"]);
        Assert.NotNull(notice.EventId);
    }

    [Fact]
    public async Task AZoneThisMachineDoesNotKnow_FallsBackToUtc()
    {
        _timezones.Setup(t => t.ResolveForUserAsync(Tenant, _ceo.UserId, It.IsAny<CancellationToken>())).ReturnsAsync("Mars/Olympus");

        await Sut().BlockWrittenAsync(Tenant, _ceo.Id, _secretary.Id, PlannerDelegateActions.Planned, Guid.NewGuid(), "Board pack", Start, End,
            CancellationToken.None);

        Assert.Equal("Tuesday, November 10, 9:45 AM – 10:45 AM (UTC)", Assert.Single(_sent).Placeholders["when"]);
    }

    [Fact]
    public void TheThreeEvents_AreInTheCatalog_AsWorkManagementInAppEvents()
    {
        foreach (var code in new[] { "planner_block_planned_for_you", "planner_block_moved_for_you", "planner_block_removed_for_you" })
        {
            var definition = Assert.Single(NotificationEventCatalog.All, d => d.Key == code);
            Assert.Equal("work_management", definition.Module);
            Assert.Equal(["delegateName", "taskTitle", "when"], definition.AllowedVariables.Select(v => v.Key).Order());
        }
    }
}
