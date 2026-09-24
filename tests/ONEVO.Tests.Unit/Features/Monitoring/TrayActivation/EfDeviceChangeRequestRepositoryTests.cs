using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;

public sealed class EfDeviceChangeRequestRepositoryTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LegalEntityId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid EmployeeId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task UpsertPendingAsync_WhenNoPendingExists_InsertsNewRow()
    {
        await using var db = BuildInMemoryDb();
        var repository = new EfDeviceChangeRequestRepository(db);
        var request = NewRequest(fingerprint: "fp-new", name: "New PC");

        await repository.UpsertPendingAsync(request);
        await repository.SaveChangesAsync();

        var saved = await repository.GetTrackedByIdAsync(TenantId, request.Id);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(DeviceChangeRequest.StatusPending);
    }

    [Fact]
    public async Task UpsertPendingAsync_WhenPendingAlreadyExists_UpdatesInPlaceInsteadOfDuplicating()
    {
        await using var db = BuildInMemoryDb();
        var repository = new EfDeviceChangeRequestRepository(db);

        var first = NewRequest(fingerprint: "fp-1", name: "PC 1");
        await repository.UpsertPendingAsync(first);
        await repository.SaveChangesAsync();

        var second = NewRequest(fingerprint: "fp-2", name: "PC 2");
        await repository.UpsertPendingAsync(second);
        await repository.SaveChangesAsync();

        var (items, totalCount) = await repository.ListApprovalInboxAsync(
            TenantId, LegalEntityId, new[] { EmployeeId }, 0, 10);

        totalCount.Should().Be(1);
        items.Should().ContainSingle();
        items[0].NewDeviceFingerprint.Should().Be("fp-2");
    }

    [Fact]
    public async Task ListPendingEmployeeIdsAsync_IgnoresRequestsForAnotherLegalEntityOrTenant()
    {
        await using var db = BuildInMemoryDb();
        db.DeviceChangeRequests.AddRange(
            NewRequest(fingerprint: "fp-a", name: "A", tenantId: OtherTenantId),
            NewRequest(fingerprint: "fp-b", name: "B", legalEntityId: Guid.NewGuid()));
        await db.SaveChangesAsync();

        var result = await new EfDeviceChangeRequestRepository(db)
            .ListPendingEmployeeIdsAsync(TenantId, LegalEntityId);

        result.Should().BeEmpty();
    }

    private static DeviceChangeRequest NewRequest(
        string fingerprint, string name, Guid? tenantId = null, Guid? legalEntityId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? TenantId,
            EmployeeId = EmployeeId,
            LegalEntityId = legalEntityId ?? LegalEntityId,
            NewDeviceFingerprint = fingerprint,
            NewDeviceName = name,
            NewDeviceOs = "Windows",
            RequestedAt = DateTimeOffset.UtcNow,
        };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(currentUser.Object, dateTime.Object),
            new SoftDeleteInterceptor(dateTime.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
