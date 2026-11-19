using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Services.Inbox;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Services.Inbox;

public class InboxEscalationSweepJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    private static Tenant TenantRow(Guid id) => new() { Id = id, Name = "T", Slug = $"t{id:N}", Status = TenantStatus.Active };

    [Fact]
    public async Task Each_active_tenant_is_swept_by_every_source_under_its_own_context_and_one_failure_does_not_stop_the_rest()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var log = new List<string>();

        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TenantRow(tenantA), TenantRow(tenantB)]);
        var switcher = new Mock<ITenantContextSwitcher>();
        switcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback((TenantRegistryEntry e, CancellationToken _) => log.Add($"switch:{e.TenantId}"))
            .Returns(Task.CompletedTask);

        var failing = new Mock<IInboxEscalationSweepSource>();
        failing.SetupGet(s => s.Source).Returns("monitoring");
        failing.Setup(s => s.EscalateOverdueAsync(It.IsAny<Guid>(), Now, It.IsAny<CancellationToken>()))
            .Callback((Guid t, DateTimeOffset _, CancellationToken _) => log.Add($"monitoring:{t}"))
            .ThrowsAsync(new InvalidOperationException("bad row"));
        var calendar = new Mock<IInboxEscalationSweepSource>();
        calendar.SetupGet(s => s.Source).Returns("calendar");
        calendar.Setup(s => s.EscalateOverdueAsync(It.IsAny<Guid>(), Now, It.IsAny<CancellationToken>()))
            .Callback((Guid t, DateTimeOffset _, CancellationToken _) => log.Add($"calendar:{t}"))
            .ReturnsAsync(1);
        var uow = new Mock<IUnitOfWork>();

        var tenantContext = new TenantContextAccessor();
        var services = new ServiceCollection();
        services.AddSingleton(tenants.Object);
        services.AddSingleton(switcher.Object);
        services.AddSingleton<IWritableTenantContext>(tenantContext);
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider { UtcNow = Now });
        services.AddSingleton(failing.Object);
        services.AddSingleton(calendar.Object);
        services.AddSingleton(uow.Object);

        var job = new InboxEscalationSweepJob(services.BuildServiceProvider(), NullLogger<InboxEscalationSweepJob>.Instance);
        var escalated = await job.RunOnceAsync(CancellationToken.None);

        escalated.Should().Be(2);
        log.Should().Equal(
            $"switch:{tenantA}", $"monitoring:{tenantA}", $"calendar:{tenantA}",
            $"switch:{tenantB}", $"monitoring:{tenantB}", $"calendar:{tenantB}");
        uow.Verify(u => u.ClearTracking(), Times.Exactly(2));
    }

    [Fact]
    public async Task A_tenant_whose_context_switch_fails_is_skipped_with_its_tracking_cleared_and_the_next_tenant_is_still_swept()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var log = new List<string>();

        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TenantRow(tenantA), TenantRow(tenantB)]);
        var switcher = new Mock<ITenantContextSwitcher>();
        switcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback((TenantRegistryEntry e, CancellationToken _) => log.Add($"switch:{e.TenantId}"))
            .Returns(Task.CompletedTask);
        switcher.Setup(s => s.SwitchToTenantAsync(It.Is<TenantRegistryEntry>(e => e.TenantId == tenantA), It.IsAny<CancellationToken>()))
            .Callback(() => log.Add($"switch:{tenantA}"))
            .ThrowsAsync(new InvalidOperationException("tenant database unreachable"));
        var calendar = new Mock<IInboxEscalationSweepSource>();
        calendar.SetupGet(s => s.Source).Returns("calendar");
        calendar.Setup(s => s.EscalateOverdueAsync(It.IsAny<Guid>(), Now, It.IsAny<CancellationToken>()))
            .Callback((Guid t, DateTimeOffset _, CancellationToken _) => log.Add($"calendar:{t}"))
            .ReturnsAsync(1);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ClearTracking()).Callback(() => log.Add("clear"));

        var services = new ServiceCollection();
        services.AddSingleton(tenants.Object);
        services.AddSingleton(switcher.Object);
        services.AddSingleton<IWritableTenantContext>(new TenantContextAccessor());
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider { UtcNow = Now });
        services.AddSingleton(calendar.Object);
        services.AddSingleton(uow.Object);

        var job = new InboxEscalationSweepJob(services.BuildServiceProvider(), NullLogger<InboxEscalationSweepJob>.Instance);
        var escalated = await job.RunOnceAsync(CancellationToken.None);

        escalated.Should().Be(1);
        log.Should().Equal($"switch:{tenantA}", "clear", $"switch:{tenantB}", $"calendar:{tenantB}");
    }
}
