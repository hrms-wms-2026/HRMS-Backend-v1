using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarNotificationSenderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IEmployeeRepository> _employees = new();

    private CalendarNotificationSender BuildSut()
    {
        _employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, Email = "ada@example.com", FirstName = "Ada", LastName = "Lovelace" });
        return new CalendarNotificationSender(_outbox.Object, _employees.Object);
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_EnqueuesInAppNotificationAndEmail()
    {
        var sut = BuildSut();
        await sut.NotifyParticipantsAddedAsync(TenantId, "Standup", DateTimeOffset.UtcNow, "Room 4", [EmployeeId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.RecipientUserId == UserId && p.TemplateCode == "calendar_event_participant_added"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "ada@example.com"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyEventUpdatedAsync_EnqueuesOnlyInAppNotification()
    {
        var sut = BuildSut();
        await sut.NotifyEventUpdatedAsync(TenantId, "Standup", [EmployeeId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.TemplateCode == "calendar_event_updated"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueAsync(OutboxMessageTypes.CalendarEventInviteEmail, It.IsAny<CalendarEventInviteEmailPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyEventCancelledAsync_EnqueuesOnlyInAppNotification()
    {
        var sut = BuildSut();
        await sut.NotifyEventCancelledAsync(TenantId, "Standup", [EmployeeId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.TemplateCode == "calendar_event_cancelled"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SkipsAnEmployeeIdThatNoLongerResolves()
    {
        var sut = BuildSut();
        var missingId = Guid.NewGuid();
        _employees.Setup(x => x.GetByIdAsync(TenantId, missingId, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        await sut.NotifyEventUpdatedAsync(TenantId, "Standup", [missingId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
