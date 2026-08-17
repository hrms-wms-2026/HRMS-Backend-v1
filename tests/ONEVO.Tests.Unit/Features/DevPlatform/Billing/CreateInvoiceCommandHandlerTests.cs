using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateInvoice;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class CreateInvoiceCommandHandlerTests
{
    private readonly ISubscriptionInvoiceRepository _invoiceRepo = Substitute.For<ISubscriptionInvoiceRepository>();
    private readonly IBillingAuditLogRepository _auditLogRepo = Substitute.For<IBillingAuditLogRepository>();
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly ICurrentPlatformUserContext _platformUser = Substitute.For<ICurrentPlatformUserContext>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly CreateInvoiceCommandHandler _handler;
    private readonly DateTimeOffset _now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();

    public CreateInvoiceCommandHandlerTests()
    {
        _clock.UtcNow.Returns(_now);
        _platformUser.UserId.Returns(_adminUserId);
        _tenantRepo.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Tenant { Id = _tenantId, Name = "Acme", Slug = "acme" });
        _invoiceRepo.GetByInvoiceNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SubscriptionInvoice?)null);

        _handler = new CreateInvoiceCommandHandler(
            _invoiceRepo,
            _auditLogRepo,
            _tenantRepo,
            _platformUser,
            _unitOfWork,
            _clock);
    }

    private CreateInvoiceCommand Command(
        decimal subtotal = 100m,
        decimal tax = 10m,
        decimal discount = 5m,
        string status = "open",
        DateTimeOffset? issuedAt = null) =>
        new(
            _tenantId,
            null,
            "USD",
            subtotal,
            tax,
            discount,
            null,
            null,
            issuedAt,
            null,
            status);

    [Fact]
    public async Task Handle_ComputesTotalAmount()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SubtotalAmount.Should().Be(100m);
        result.Value.TaxAmount.Should().Be(10m);
        result.Value.DiscountAmount.Should().Be(5m);
        result.Value.TotalAmount.Should().Be(105m);
    }

    [Fact]
    public async Task Handle_DiscountExceedsSubtotalPlusTax_ReturnsFailure()
    {
        var result = await _handler.Handle(Command(discount: 200m), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("negative");
    }

    [Fact]
    public async Task Handle_InvalidStatus_ReturnsFailure()
    {
        var result = await _handler.Handle(Command(status: "cancelled"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_OpenWithoutIssuedAt_SetsIssuedAtToNow()
    {
        var result = await _handler.Handle(Command(issuedAt: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IssuedAt.Should().Be(_now);
    }

    [Fact]
    public async Task Handle_CreatesAuditLog()
    {
        await _handler.Handle(Command(), CancellationToken.None);

        await _auditLogRepo.Received(1).AddAsync(
            Arg.Is<BillingAuditLog>(l =>
                l.Action == "invoice.created" &&
                l.TenantId == _tenantId &&
                l.ActorAdminUserId == _adminUserId),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownTenant_ReturnsNotFound()
    {
        var unknownTenantId = Guid.NewGuid();
        var result = await _handler.Handle(
            Command() with { TenantId = unknownTenantId },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
