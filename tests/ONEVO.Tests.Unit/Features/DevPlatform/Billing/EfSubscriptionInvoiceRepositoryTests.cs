using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Billing;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class EfSubscriptionInvoiceRepositoryTests
{
    [Fact]
    public async Task GetByInvoiceNumberAsync_FindsMatchingInvoice()
    {
        await using var db = BuildInMemoryDb();
        var expected = CreateInvoice("INV-1001");
        db.SubscriptionInvoices.AddRange(expected, CreateInvoice("INV-1002"));
        await db.SaveChangesAsync();

        var repository = new EfSubscriptionInvoiceRepository(db);

        var invoice = await repository.GetByInvoiceNumberAsync("INV-1001", CancellationToken.None);

        Assert.NotNull(invoice);
        Assert.Equal(expected.Id, invoice.Id);
    }

    [Fact]
    public async Task GetByExternalInvoiceIdAsync_FindsMatchingInvoice()
    {
        await using var db = BuildInMemoryDb();
        var expected = CreateInvoice("INV-2001", externalInvoiceId: "stripe_inv_123");
        db.SubscriptionInvoices.Add(expected);
        await db.SaveChangesAsync();

        var repository = new EfSubscriptionInvoiceRepository(db);

        var invoice = await repository.GetByExternalInvoiceIdAsync("stripe_inv_123", CancellationToken.None);

        Assert.NotNull(invoice);
        Assert.Equal(expected.Id, invoice.Id);
    }

    [Fact]
    public async Task ListAsync_AppliesTenantStatusAndDateFilters()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var inRange = CreateInvoice("INV-3001", tenantId: tenantId, status: "open");
        inRange.CreatedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        var wrongStatus = CreateInvoice("INV-3002", tenantId: tenantId, status: "paid");
        wrongStatus.CreatedAt = inRange.CreatedAt;

        var wrongTenant = CreateInvoice("INV-3003", tenantId: otherTenantId, status: "open");
        wrongTenant.CreatedAt = inRange.CreatedAt;

        var outOfRange = CreateInvoice("INV-3004", tenantId: tenantId, status: "open");
        outOfRange.CreatedAt = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        db.SubscriptionInvoices.AddRange(inRange, wrongStatus, wrongTenant, outOfRange);
        await db.SaveChangesAsync();

        var repository = new EfSubscriptionInvoiceRepository(db);

        var results = await repository.ListAsync(
            new SubscriptionInvoiceListFilter(
                tenantId,
                "open",
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
                Skip: 0,
                Take: 10),
            CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(inRange.Id, results[0].Id);
    }

    [Fact]
    public async Task CountAsync_AppliesSameFiltersAsListAsync()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var match = CreateInvoice("INV-5001", tenantId: tenantId, status: "open");
        match.CreatedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        var other = CreateInvoice("INV-5002", tenantId: tenantId, status: "paid");
        other.CreatedAt = match.CreatedAt;

        db.SubscriptionInvoices.AddRange(match, other);
        await db.SaveChangesAsync();

        var repository = new EfSubscriptionInvoiceRepository(db);
        var filter = new SubscriptionInvoiceListFilter(
            tenantId,
            "open",
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            Skip: 0,
            Take: 10);

        var count = await repository.CountAsync(filter, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ListByTenantAsync_ReturnsOnlyTenantInvoicesOrderedByCreatedAtDesc()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var older = CreateInvoice("INV-4001", tenantId: tenantId);
        older.CreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        var newer = CreateInvoice("INV-4002", tenantId: tenantId);
        newer.CreatedAt = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

        var otherTenant = CreateInvoice("INV-4003", tenantId: Guid.NewGuid());

        db.SubscriptionInvoices.AddRange(older, newer, otherTenant);
        await db.SaveChangesAsync();

        var repository = new EfSubscriptionInvoiceRepository(db);

        var results = await repository.ListByTenantAsync(tenantId, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(newer.Id, results[0].Id);
        Assert.Equal(older.Id, results[1].Id);
    }

    private static SubscriptionInvoice CreateInvoice(
        string invoiceNumber,
        Guid? tenantId = null,
        string status = "draft",
        string? externalInvoiceId = null)
    {
        return new SubscriptionInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            ExternalInvoiceId = externalInvoiceId,
            Status = status,
            Currency = "USD",
            SubtotalAmount = 100m,
            TaxAmount = 0m,
            DiscountAmount = 0m,
            TotalAmount = 100m,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(
            currentUser.Object,
            dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options,
            auditInterceptor,
            softDeleteInterceptor,
            domainEventInterceptor,
            tenantContext.Object);
    }
}
