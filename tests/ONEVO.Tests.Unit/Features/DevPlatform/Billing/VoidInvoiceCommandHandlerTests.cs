using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.VoidInvoice;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class VoidInvoiceCommandHandlerTests
{
    private readonly ISubscriptionInvoiceRepository _invoiceRepo = Substitute.For<ISubscriptionInvoiceRepository>();
    private readonly IBillingAuditLogRepository _auditLogRepo = Substitute.For<IBillingAuditLogRepository>();
    private readonly ICurrentPlatformUserContext _platformUser = Substitute.For<ICurrentPlatformUserContext>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly VoidInvoiceCommandHandler _handler;
    private readonly DateTimeOffset _now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    public VoidInvoiceCommandHandlerTests()
    {
        _clock.UtcNow.Returns(_now);
        _handler = new VoidInvoiceCommandHandler(
            _invoiceRepo,
            _auditLogRepo,
            _platformUser,
            _unitOfWork,
            _clock);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("open")]
    public async Task Handle_AllowedStatus_VoidsInvoice(string status)
    {
        var invoiceId = Guid.NewGuid();
        var invoice = new SubscriptionInvoice
        {
            Id = invoiceId,
            TenantId = Guid.NewGuid(),
            InvoiceNumber = "INV-1",
            Status = status,
            Currency = "USD",
            SubtotalAmount = 100m,
            TotalAmount = 100m,
            CreatedAt = _now
        };

        _invoiceRepo.GetByIdAsync(invoiceId, Arg.Any<CancellationToken>()).Returns(invoice);
        _auditLogRepo.ListByInvoiceAsync(invoiceId, Arg.Any<CancellationToken>())
            .Returns(new List<BillingAuditLog>());

        var result = await _handler.Handle(new VoidInvoiceCommand(invoiceId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("void");
        result.Value.VoidedAt.Should().Be(_now);
        await _auditLogRepo.Received(1).AddAsync(
            Arg.Is<BillingAuditLog>(l => l.Action == "invoice.voided"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("paid")]
    [InlineData("void")]
    public async Task Handle_DisallowedStatus_ReturnsConflict(string status)
    {
        var invoiceId = Guid.NewGuid();
        _invoiceRepo.GetByIdAsync(invoiceId, Arg.Any<CancellationToken>()).Returns(new SubscriptionInvoice
        {
            Id = invoiceId,
            TenantId = Guid.NewGuid(),
            InvoiceNumber = "INV-2",
            Status = status,
            Currency = "USD",
            SubtotalAmount = 100m,
            TotalAmount = 100m,
            CreatedAt = _now
        });

        var result = await _handler.Handle(new VoidInvoiceCommand(invoiceId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }
}
