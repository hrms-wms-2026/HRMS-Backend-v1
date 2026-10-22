using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeaveLedgerArchitectureTests
{
    [Fact]
    public void LedgerRows_CarryPolicyVersionAndMonthCoverage()
    {
        using var context = CreateModelInspectionContext();
        var audit = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LeaveBalanceAudit))!;

        foreach (var name in new[]
                 {
                     nameof(LeaveBalanceAudit.LeavePolicyId), nameof(LeaveBalanceAudit.LeavePolicyVersionId),
                     nameof(LeaveBalanceAudit.Year), nameof(LeaveBalanceAudit.CoversFromMonth), nameof(LeaveBalanceAudit.CoversToMonth)
                 })
            Assert.True(audit.FindProperty(name)!.IsNullable, name);

        var check = Assert.Single(audit.GetCheckConstraints());
        Assert.Contains("covers_from_month", check.Sql);
        Assert.Contains("covers_to_month", check.Sql);
    }

    [Fact]
    public void Entitlement_HasXminConcurrencyToken_AndPolicyVersion()
    {
        using var context = CreateModelInspectionContext();
        var entitlement = context.Model.FindEntityType(typeof(LeaveEntitlement))!;

        var xmin = entitlement.FindProperty("xmin");
        Assert.NotNull(xmin);
        Assert.True(xmin!.IsConcurrencyToken);
        Assert.NotNull(entitlement.FindProperty(nameof(LeaveEntitlement.LeavePolicyVersionId)));
    }

    [Fact]
    public void Request_RecordsPolicyVersionAndOverBalanceHours()
    {
        using var context = CreateModelInspectionContext();
        var request = context.Model.FindEntityType(typeof(LeaveRequest))!;

        Assert.True(request.FindProperty(nameof(LeaveRequest.LeavePolicyVersionId))!.IsNullable);
        Assert.Equal("numeric(8,2)", request.FindProperty(nameof(LeaveRequest.ExceedsBalanceByHours))!.GetColumnType());
    }

    private static ApplicationDbContext CreateModelInspectionContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }
}
