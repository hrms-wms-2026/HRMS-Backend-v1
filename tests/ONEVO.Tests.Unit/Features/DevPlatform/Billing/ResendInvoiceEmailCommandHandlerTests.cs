using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.ResendInvoiceEmail;
using ONEVO.Application.Features.DevPlatform.Billing.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class ResendInvoiceEmailCommandHandlerTests
{
    private readonly ISubscriptionInvoiceRepository _invoiceRepo = Substitute.For<ISubscriptionInvoiceRepository>();
    private readonly IBillingAuditLogRepository _auditLogRepo = Substitute.For<IBillingAuditLogRepository>();
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly ILegalEntityRepository _legalEntities = Substitute.For<ILegalEntityRepository>();
    private readonly IInvitationTokenRepository _invitations = Substitute.For<IInvitationTokenRepository>();
    private readonly ICurrentPlatformUserContext _platformUser = Substitute.For<ICurrentPlatformUserContext>();
    private readonly IOutboxWriter _outbox = Substitute.For<IOutboxWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ResendInvoiceEmailCommandHandler _handler;
    private readonly DateTimeOffset _now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _invoiceId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();

    public ResendInvoiceEmailCommandHandlerTests()
    {
        _clock.UtcNow.Returns(_now);
        _platformUser.UserId.Returns(_adminUserId);
        _tenantRepo.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Tenant { Id = _tenantId, Name = "Acme Co", Slug = "acme-co" });
        _legalEntities.GetPrimaryByTenantIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new LegalEntity { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "billing@acme.com", Name = "Acme Legal" });
        _handler = new ResendInvoiceEmailCommandHandler(
            _invoiceRepo,
            _auditLogRepo,
            _tenantRepo,
            _legalEntities,
            _invitations,
            _platformUser,
            _outbox,
            _unitOfWork,
            _clock);
    }

    [Fact]
    public async Task Handle_OpenInvoice_QueuesEmailAndCreatesAuditLog()
    {
        _invoiceRepo.GetByIdAsync(_invoiceId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoice("open"));

        var result = await _handler.Handle(new ResendInvoiceEmailCommand(_invoiceId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DeliveryStatus.Should().Be("queued");
        result.Value.RecipientEmail.Should().Be("billing@acme.com");

        await _outbox.Received(1).EnqueueAsync(
            OutboxMessageTypes.InvoiceEmail,
            Arg.Is<InvoiceEmailPayload>(p =>
                p.InvoiceId == _invoiceId &&
                p.Status == "open" &&
                !p.IsReceipt),
            _tenantId,
            Arg.Any<CancellationToken>());

        await _auditLogRepo.Received(1).AddAsync(
            Arg.Is<BillingAuditLog>(l => l.Action == "invoice.email_resent"),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PaidInvoice_QueuesReceiptEmail()
    {
        _invoiceRepo.GetByIdAsync(_invoiceId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoice("paid", paidAt: _now));

        var result = await _handler.Handle(new ResendInvoiceEmailCommand(_invoiceId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _outbox.Received(1).EnqueueAsync(
            OutboxMessageTypes.InvoiceEmail,
            Arg.Is<InvoiceEmailPayload>(p => p.IsReceipt && p.Status == "paid"),
            _tenantId,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("void")]
    public async Task Handle_DisallowedStatus_ReturnsConflict(string status)
    {
        _invoiceRepo.GetByIdAsync(_invoiceId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoice(status));

        var result = await _handler.Handle(new ResendInvoiceEmailCommand(_invoiceId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        await _outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingInvoice_ReturnsNotFound()
    {
        var result = await _handler.Handle(new ResendInvoiceEmailCommand(_invoiceId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    private SubscriptionInvoice BuildInvoice(string status, DateTimeOffset? paidAt = null) => new()
    {
        Id = _invoiceId,
        TenantId = _tenantId,
        InvoiceNumber = "INV-1001",
        Status = status,
        Currency = "USD",
        SubtotalAmount = 100m,
        TaxAmount = 10m,
        DiscountAmount = 0m,
        TotalAmount = 110m,
        PeriodStart = new DateOnly(2026, 8, 1),
        PeriodEnd = new DateOnly(2026, 8, 31),
        DueAt = _now.AddDays(7),
        PaidAt = paidAt,
        CreatedAt = _now
    };
}
