using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Features.DevPlatform.Billing.Queries.ListInvoices;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class ListInvoicesQueryHandlerTests
{
    private readonly ISubscriptionInvoiceRepository _invoiceRepo = Substitute.For<ISubscriptionInvoiceRepository>();
    private readonly ListInvoicesQueryHandler _handler;

    public ListInvoicesQueryHandlerTests() => _handler = new ListInvoicesQueryHandler(_invoiceRepo);

    [Fact]
    public async Task Handle_AppliesFiltersAndPagination()
    {
        var tenantId = Guid.NewGuid();
        var invoice = new SubscriptionInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = "INV-100",
            Status = "open",
            Currency = "USD",
            SubtotalAmount = 100m,
            TotalAmount = 100m,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _invoiceRepo.ListAsync(Arg.Any<SubscriptionInvoiceListFilter>(), Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionInvoice> { invoice });
        _invoiceRepo.CountAsync(Arg.Any<SubscriptionInvoiceListFilter>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var result = await _handler.Handle(
            new ListInvoicesQuery(tenantId, "open", from, to, 2, 10, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Total.Should().Be(1);
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(10);
        result.Value.Items.Should().ContainSingle(i => i.Id == invoice.Id);

        await _invoiceRepo.Received(1).ListAsync(
            Arg.Is<SubscriptionInvoiceListFilter>(f =>
                f.TenantId == tenantId &&
                f.Status == "open" &&
                f.From == from &&
                f.To == to &&
                f.Skip == 10 &&
                f.Take == 10),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidStatus_ReturnsFailure()
    {
        var result = await _handler.Handle(
            new ListInvoicesQuery(null, "cancelled", null, null, 1, 25, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_SkipTakeOverride_UsesDirectPagination()
    {
        _invoiceRepo.ListAsync(Arg.Any<SubscriptionInvoiceListFilter>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SubscriptionInvoice>());
        _invoiceRepo.CountAsync(Arg.Any<SubscriptionInvoiceListFilter>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _handler.Handle(
            new ListInvoicesQuery(null, null, null, null, 1, 25, 50, 20),
            CancellationToken.None);

        await _invoiceRepo.Received(1).ListAsync(
            Arg.Is<SubscriptionInvoiceListFilter>(f => f.Skip == 50 && f.Take == 20),
            Arg.Any<CancellationToken>());
    }
}
