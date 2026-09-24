using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Webhooks.Stripe;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class ProcessStripeEventCommandHandlerTests
{
    private readonly ITenantSubscriptionRepository _subscriptions = Substitute.For<ITenantSubscriptionRepository>();
    private readonly ISubscriptionInvoiceRepository _invoiceRepo = Substitute.For<ISubscriptionInvoiceRepository>();
    private readonly IBillingAuditLogRepository _auditLogRepo = Substitute.For<IBillingAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ProcessStripeEventCommandHandler _handler;
    private readonly DateTimeOffset _now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _subscriptionId = Guid.NewGuid();
    private const string GatewaySubRef = "sub_stripe_123";
    private const string ExternalInvoiceId = "in_stripe_456";

    public ProcessStripeEventCommandHandlerTests()
    {
        _clock.UtcNow.Returns(_now);
        _handler = new ProcessStripeEventCommandHandler(
            _subscriptions,
            _invoiceRepo,
            _auditLogRepo,
            _unitOfWork,
            _clock);
    }

    [Fact]
    public async Task Handle_InvoicePaymentSucceeded_CreatesPaidInvoiceAndActivatesSubscription()
    {
        var subscription = BuildSubscription();
        _subscriptions.GetByGatewaySubscriptionRefAsync(GatewaySubRef, Arg.Any<CancellationToken>())
            .Returns(subscription);
        _invoiceRepo.GetByExternalInvoiceIdAsync(ExternalInvoiceId, Arg.Any<CancellationToken>())
            .Returns((SubscriptionInvoice?)null);
        _invoiceRepo.GetByInvoiceNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SubscriptionInvoice?)null);

        var command = BuildInvoiceCommand("invoice.payment_succeeded", "paid");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        subscription.Status.Should().Be("active");
        subscription.AccessEndsAt.Should().BeNull();

        await _invoiceRepo.Received(1).AddAsync(
            Arg.Is<SubscriptionInvoice>(i =>
                i.ExternalInvoiceId == ExternalInvoiceId &&
                i.Status == "paid" &&
                i.TenantId == _tenantId &&
                i.TenantSubscriptionId == _subscriptionId &&
                i.PaidAt == _now),
            Arg.Any<CancellationToken>());

        await _auditLogRepo.Received(1).AddAsync(
            Arg.Is<BillingAuditLog>(l => l.Action == "invoice.synced_from_stripe"),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvoicePaymentFailed_CreatesOpenInvoiceAndSetsPastDueGrace()
    {
        var subscription = BuildSubscription();
        _subscriptions.GetByGatewaySubscriptionRefAsync(GatewaySubRef, Arg.Any<CancellationToken>())
            .Returns(subscription);
        _invoiceRepo.GetByExternalInvoiceIdAsync(ExternalInvoiceId, Arg.Any<CancellationToken>())
            .Returns((SubscriptionInvoice?)null);
        _invoiceRepo.GetByInvoiceNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SubscriptionInvoice?)null);

        var command = BuildInvoiceCommand("invoice.payment_failed", paidAt: null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        subscription.Status.Should().Be("past_due");
        subscription.AccessEndsAt.Should().Be(_now.AddDays(7));

        await _invoiceRepo.Received(1).AddAsync(
            Arg.Is<SubscriptionInvoice>(i =>
                i.ExternalInvoiceId == ExternalInvoiceId &&
                i.Status == "open"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepeatedExternalInvoiceId_UpdatesExistingInvoiceWithoutDuplicate()
    {
        var subscription = BuildSubscription();
        var existingInvoice = new SubscriptionInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TenantSubscriptionId = _subscriptionId,
            ExternalInvoiceId = ExternalInvoiceId,
            InvoiceNumber = "INV-STRIPE-001",
            Status = "open",
            Currency = "USD",
            SubtotalAmount = 80m,
            TaxAmount = 0m,
            DiscountAmount = 0m,
            TotalAmount = 80m,
            CreatedAt = _now.AddDays(-1)
        };

        _subscriptions.GetByGatewaySubscriptionRefAsync(GatewaySubRef, Arg.Any<CancellationToken>())
            .Returns(subscription);
        _invoiceRepo.GetByExternalInvoiceIdAsync(ExternalInvoiceId, Arg.Any<CancellationToken>())
            .Returns(existingInvoice);

        var command = BuildInvoiceCommand("invoice.payment_succeeded", "paid", totalAmount: 105m);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existingInvoice.Status.Should().Be("paid");
        existingInvoice.TotalAmount.Should().Be(105m);

        await _invoiceRepo.DidNotReceive().AddAsync(Arg.Any<SubscriptionInvoice>(), Arg.Any<CancellationToken>());
        await _invoiceRepo.Received(1).UpdateAsync(existingInvoice, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingSubscription_DoesNotCreateInvoice()
    {
        _subscriptions.GetByGatewaySubscriptionRefAsync(GatewaySubRef, Arg.Any<CancellationToken>())
            .Returns((TenantSubscription?)null);

        var command = BuildInvoiceCommand("invoice.payment_succeeded", "paid");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _invoiceRepo.DidNotReceive().AddAsync(Arg.Any<SubscriptionInvoice>(), Arg.Any<CancellationToken>());
        await _invoiceRepo.DidNotReceive().UpdateAsync(Arg.Any<SubscriptionInvoice>(), Arg.Any<CancellationToken>());
        await _auditLogRepo.DidNotReceive().AddAsync(Arg.Any<BillingAuditLog>(), Arg.Any<CancellationToken>());
    }

    private TenantSubscription BuildSubscription() => new()
    {
        Id = _subscriptionId,
        TenantId = _tenantId,
        PlanId = Guid.NewGuid(),
        GatewaySubscriptionRef = GatewaySubRef,
        Status = "active",
        BillingCurrency = "USD",
        UnpaidGracePeriodDays = 7,
        CreatedAt = _now
    };

    private ProcessStripeEventCommand BuildInvoiceCommand(
        string eventType,
        string? _ = null,
        decimal totalAmount = 100m,
        DateTimeOffset? paidAt = null) =>
        new(
            eventType,
            GatewaySubRef,
            "cus_stripe_123",
            null,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            ExternalInvoiceId,
            "INV-STRIPE-001",
            "stripe",
            "USD",
            90m,
            10m,
            0m,
            totalAmount,
            _now.AddDays(-1),
            _now.AddDays(14),
            paidAt ?? _now);
}
