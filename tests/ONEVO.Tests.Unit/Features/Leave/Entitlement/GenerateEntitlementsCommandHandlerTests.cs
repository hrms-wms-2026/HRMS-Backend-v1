using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Commands.GenerateEntitlements;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class GenerateEntitlementsCommandHandlerTests
{
    [Fact]
    public async Task Handle_CreatesEntitlementsAndAuditRows()
    {
        var captured = await GenerateAsync(19m, 3m, workWindowSet: true);

        captured.Result.IsSuccess.Should().BeTrue();
        captured.WriteSets.Should().ContainSingle();
        captured.WriteSets!.Single().Entitlement.TotalHours.Should().Be(152.00m);
        captured.WriteSets.Single().Audits.Should().Contain(a => a.ChangeType == LeaveBalanceChangeTypes.Accrual);
    }

    [Fact]
    public async Task Handle_FourteenDaysTimesEightHourWindow_Yields112Hours()
    {
        var captured = await GenerateAsync(14m, 0m, workWindowSet: true);

        captured.Result.IsSuccess.Should().BeTrue();
        captured.WriteSets.Should().ContainSingle();
        captured.WriteSets!.Single().Entitlement.TotalHours.Should().Be(112.00m);
        captured.WriteSets.Single().Entitlement.CarriedForwardHours.Should().Be(0m);
        captured.Result.Value!.Created.Should().ContainSingle(l => l.TotalHours == 112.00m);
    }

    [Fact]
    public async Task Handle_WhenWorkWindowUnset_SkipsWithWorkWindowRequired()
    {
        var captured = await GenerateAsync(14m, 0m, workWindowSet: false);

        captured.Result.IsSuccess.Should().BeTrue();
        captured.WriteSets.Should().BeNull();
        captured.Result.Value!.CreatedCount.Should().Be(0);
        captured.Result.Value.Skipped.Should().Contain(s => s.Reason == LeaveRequestMessages.WorkWindowRequired);
    }

    private static async Task<(ONEVO.Application.Common.Models.Result<ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses.LeaveEntitlementGenerationResultResponse> Result, IReadOnlyCollection<LeaveEntitlementWriteSet>? WriteSets)>
        GenerateAsync(decimal annualEntitlementDays, decimal? carryForwardMaxDays, bool workWindowSet)
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = PreviewGenerateEntitlementsQueryHandlerTests.CreateEmployee(tenantId, legalEntityId);
        var policy = PreviewGenerateEntitlementsQueryHandlerTests.CreatePolicyAggregate(
            tenantId, legalEntityId, annualEntitlementDays, carryForwardMaxDays);
        IReadOnlyCollection<LeaveEntitlementWriteSet>? captured = null;

        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var employees = new Mock<IEmployeeRepository>();
        var policies = new Mock<ILeavePolicyRepository>();
        var entitlements = new Mock<ILeaveEntitlementRepository>();

        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));
        employees.Setup(x => x.ListActiveByLegalEntityAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([employee]);
        policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate> { [legalEntityId] = policy });
        policies.Setup(x => x.ListActiveLegalEntitiesByIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([PreviewGenerateEntitlementsQueryHandlerTests.CreateLegalEntity(tenantId, legalEntityId, workWindowSet)]);
        entitlements.Setup(x => x.ListExistingAsync(tenantId, 2026, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        entitlements.Setup(x => x.ListPreviousYearAsync(tenantId, 2025, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(Guid, Guid), LeaveEntitlement>());
        employees.Setup(x => x.ListLegalEntityChangeWarningsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
        entitlements.Setup(x => x.AddGeneratedAsync(It.IsAny<IReadOnlyCollection<LeaveEntitlementWriteSet>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<LeaveEntitlementWriteSet>, CancellationToken>((sets, _) => captured = sets)
            .Returns(Task.CompletedTask);

        var handler = new GenerateEntitlementsCommandHandler(
            currentUser.Object,
            dateTime.Object,
            new LeaveEntitlementPlanner(
                employees.Object,
                policies.Object,
                entitlements.Object,
                new LeaveEntitlementCalculator(new LeaveWorkingDayCounter())),
            entitlements.Object);

        var result = await handler.Handle(new GenerateEntitlementsCommand(2026, legalEntityId), CancellationToken.None);
        return (result, captured);
    }
}
