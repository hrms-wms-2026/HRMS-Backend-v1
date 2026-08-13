using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Billing;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class EfBillingAuditLogRepositoryTests
{
    [Fact]
    public async Task ListByInvoiceAsync_ReturnsLogsOrderedByCreatedAtAsc()
    {
        await using var db = BuildInMemoryDb();
        var invoiceId = Guid.NewGuid();
        var first = CreateLog(invoiceId: invoiceId, action: "invoice.created");
        first.CreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        var second = CreateLog(invoiceId: invoiceId, action: "invoice.issued");
        second.CreatedAt = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);

        var otherInvoice = CreateLog(invoiceId: Guid.NewGuid(), action: "invoice.created");

        db.BillingAuditLogs.AddRange(second, first, otherInvoice);
        await db.SaveChangesAsync();

        var repository = new EfBillingAuditLogRepository(db);

        var results = await repository.ListByInvoiceAsync(invoiceId, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(first.Id, results[0].Id);
        Assert.Equal(second.Id, results[1].Id);
    }

    [Fact]
    public async Task ListByTenantAsync_ReturnsLogsOrderedByCreatedAtDesc()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var older = CreateLog(tenantId: tenantId, action: "invoice.created");
        older.CreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        var newer = CreateLog(tenantId: tenantId, action: "invoice.paid");
        newer.CreatedAt = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

        var otherTenant = CreateLog(tenantId: Guid.NewGuid(), action: "invoice.created");

        db.BillingAuditLogs.AddRange(older, newer, otherTenant);
        await db.SaveChangesAsync();

        var repository = new EfBillingAuditLogRepository(db);

        var results = await repository.ListByTenantAsync(tenantId, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(newer.Id, results[0].Id);
        Assert.Equal(older.Id, results[1].Id);
    }

    private static BillingAuditLog CreateLog(
        Guid? tenantId = null,
        Guid? invoiceId = null,
        string action = "invoice.created")
    {
        return new BillingAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceId = invoiceId,
            Action = action,
            Message = "Test audit entry",
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
