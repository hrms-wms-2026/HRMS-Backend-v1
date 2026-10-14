using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Services.TimeAttendance;
using Xunit;

namespace ONEVO.Tests.Unit.Services.TimeAttendance;

public class AttendanceAlertMaterializerJobTests
{
    private static Tenant TenantRow(Guid id) => new() { Id = id, Name = "T", Slug = $"t{id:N}", Status = TenantStatus.Active };

    [Fact]
    public async Task Every_active_employee_of_every_active_tenant_is_materialised_under_its_tenant_and_one_failure_does_not_stop_the_rest()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var legalEntity = Guid.NewGuid();
        var ada = new Employee { Id = Guid.NewGuid(), TenantId = tenantA, LegalEntityId = legalEntity };
        var bob = new Employee { Id = Guid.NewGuid(), TenantId = tenantA, LegalEntityId = null };
        var cy = new Employee { Id = Guid.NewGuid(), TenantId = tenantB, LegalEntityId = legalEntity };
        var log = new List<string>();

        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TenantRow(tenantA), TenantRow(tenantB)]);
        var switcher = new Mock<ITenantContextSwitcher>();
        switcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback((TenantRegistryEntry e, CancellationToken _) => log.Add($"switch:{e.TenantId}"))
            .Returns(Task.CompletedTask);
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(e => e.ListActiveByLegalEntityAsync(tenantA, null, It.IsAny<CancellationToken>())).ReturnsAsync([ada, bob]);
        employees.Setup(e => e.ListActiveByLegalEntityAsync(tenantB, null, It.IsAny<CancellationToken>())).ReturnsAsync([cy]);
        var materializer = new Mock<IAttendanceAlertMaterializer>();
        materializer.Setup(m => m.MaterializeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid t, Guid e, Guid? le, CancellationToken _) => log.Add($"materialise:{e}:{le}"))
            .ReturnsAsync(new AttendanceAlertMaterializeResult(2, 1));
        materializer.Setup(m => m.MaterializeAsync(tenantA, ada.Id, legalEntity, It.IsAny<CancellationToken>()))
            .Callback(() => log.Add($"materialise:{ada.Id}:{legalEntity}"))
            .ThrowsAsync(new InvalidOperationException("bad day"));
        var uow = new Mock<IUnitOfWork>();

        var services = new ServiceCollection();
        services.AddSingleton(tenants.Object);
        services.AddSingleton(switcher.Object);
        services.AddSingleton<IWritableTenantContext>(new TenantContextAccessor());
        services.AddSingleton(employees.Object);
        services.AddSingleton(materializer.Object);
        services.AddSingleton(uow.Object);

        var job = new AttendanceAlertMaterializerJob(services.BuildServiceProvider(), NullLogger<AttendanceAlertMaterializerJob>.Instance);
        var changed = await job.RunOnceAsync(CancellationToken.None);

        changed.Should().Be(6);
        log.Should().Equal(
            $"switch:{tenantA}", $"materialise:{ada.Id}:{legalEntity}", $"materialise:{bob.Id}:",
            $"switch:{tenantB}", $"materialise:{cy.Id}:{legalEntity}");
        uow.Verify(u => u.ClearTracking(), Times.Once);
    }
}
