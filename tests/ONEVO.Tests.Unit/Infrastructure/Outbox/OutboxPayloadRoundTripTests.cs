using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Billing.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.OutboxHandlers;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Outbox;

/// <summary>
/// Task 31: the payload a writer stores (OutboxWriter serialises with the default JsonSerializer) reaches the service
/// call unchanged after the handler reads it back. Covers the handlers that had no such test.
/// </summary>
public sealed class OutboxPayloadRoundTripTests
{
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static string Write<T>(T payload) => JsonSerializer.Serialize(payload);

    [Fact]
    public async Task PasswordReset()
    {
        var json = Write(new PasswordResetEmailPayload(Guid.NewGuid(), Guid.NewGuid(), "a@x.test", "tok-1", "dapi"));

        await new PasswordResetEmailOutboxHandler(_email).HandleAsync(json, Ct);

        await _email.Received(1).SendPasswordResetAsync("a@x.test", "tok-1", "dapi", Ct);
    }

    [Fact]
    public async Task AdminPasswordReset()
    {
        await new AdminPasswordResetEmailOutboxHandler(_email).HandleAsync(Write(new AdminPasswordResetEmailPayload("b@x.test", "tok-2")), Ct);

        await _email.Received(1).SendAdminPasswordResetAsync("b@x.test", "tok-2", Ct);
    }

    [Fact]
    public async Task AdminPasswordChanged()
    {
        await new AdminPasswordChangedEmailOutboxHandler(_email).HandleAsync(Write(new AdminPasswordChangedEmailPayload("c@x.test")), Ct);

        await _email.Received(1).SendAdminPasswordChangedAsync("c@x.test", Ct);
    }

    [Fact]
    public async Task PlatformManagerInvite()
    {
        var json = Write(new PlatformManagerInviteEmailPayload("d@x.test", "Dee Example", "tok-3"));

        await new PlatformManagerInviteEmailOutboxHandler(_email).HandleAsync(json, Ct);

        await _email.Received(1).SendPlatformManagerInviteAsync("d@x.test", "Dee Example", "tok-3", Ct);
    }

    [Fact]
    public async Task EmployeeOnboardingInvite()
    {
        var json = Write(new EmployeeOnboardingInviteEmailPayload(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "e@x.test", "Eve", "Example", "tok-4",
            DateTimeOffset.UtcNow.AddDays(7), "dapi"));

        await new EmployeeOnboardingInviteEmailOutboxHandler(_email).HandleAsync(json, Ct);

        await _email.Received(1).SendEmployeeOnboardingInviteAsync("e@x.test", "Eve", "Example", "tok-4", "dapi", Ct);
    }

    [Fact]
    public async Task Invoice()
    {
        var payload = new InvoiceEmailPayload(
            Guid.NewGuid(), Guid.NewGuid(), "Acme", "f@x.test", "INV-7", "paid", "USD",
            100.50m, 10m, 5m, 105.50m, "2026-10-01", "2026-10-31", "2026-11-15", "2026-11-10", IsReceipt: true);
        object? templateData = null;
        _email.SendInvoiceEmailAsync("f@x.test", Arg.Do<object>(d => templateData = d), Ct).Returns(Task.CompletedTask);

        await new InvoiceEmailOutboxHandler(_email).HandleAsync(Write(payload), Ct);

        await _email.Received(1).SendInvoiceEmailAsync("f@x.test", Arg.Any<object>(), Ct);
        var data = JsonSerializer.SerializeToElement(templateData);
        data.GetProperty("tenant_name").GetString().Should().Be("Acme");
        data.GetProperty("invoice_number").GetString().Should().Be("INV-7");
        data.GetProperty("total_amount").GetDecimal().Should().Be(105.50m);
        data.GetProperty("currency").GetString().Should().Be("USD");
    }

    [Fact]
    public async Task CalendarOutlookPush_PushesTheEvent()
    {
        var sync = Substitute.For<ICalendarSyncService>();
        var (tenant, connection, evt) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await new CalendarOutlookPushOutboxHandler(sync).HandleAsync(Write(new CalendarOutlookPushPayload(tenant, connection, evt)), Ct);

        await sync.Received(1).PushOutlookEventAsync(tenant, connection, evt, Ct);
        await sync.DidNotReceiveWithAnyArgs().DeleteOutlookEventAsync(default, default, default!, default);
    }

    [Fact]
    public async Task CalendarOutlookPush_DeletesTheExternalEvent()
    {
        var sync = Substitute.For<ICalendarSyncService>();
        var (tenant, connection) = (Guid.NewGuid(), Guid.NewGuid());

        await new CalendarOutlookPushOutboxHandler(sync).HandleAsync(
            Write(new CalendarOutlookPushPayload(tenant, connection, null, "AAMk-ext")), Ct);

        await sync.Received(1).DeleteOutlookEventAsync(tenant, connection, "AAMk-ext", Ct);
        await sync.DidNotReceiveWithAnyArgs().PushOutlookEventAsync(default, default, default, default);
    }
}
