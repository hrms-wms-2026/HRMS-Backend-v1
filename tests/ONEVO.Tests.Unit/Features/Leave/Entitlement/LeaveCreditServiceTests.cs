using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Services;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public sealed class LeaveCreditServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ILeaveEntitlementRepository> _entitlements = new();
    private readonly Mock<ILeavePolicyResolver> _resolver = new();
    private readonly List<LeaveEntitlement> _balances = [];
    private readonly List<LeaveBalanceAudit> _audits = [];
    private readonly Employee _employee;

    public LeaveCreditServiceTests()
    {
        _employee = new Employee
        {
            Id = _employeeId, TenantId = _tenantId, EmployeeNumber = "E1",
            FirstName = "A", LastName = "B", Email = "a@test.dev", HireDate = new DateOnly(2025, 1, 1)
        };
        _employees.Setup(x => x.ListByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [_employeeId] = _employee });
        _employees.Setup(x => x.ListActiveByLegalEntityAsync(_tenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_employee]);
        _entitlements.Setup(x => x.ListExistingAsync(
                _tenantId, 2026, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _balances);
        _entitlements.Setup(x => x.ListTrackedExistingAsync(
                _tenantId, 2026, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _balances);
        _entitlements.Setup(x => x.ListAuditsAsync(
                _tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _audits);
        _entitlements.Setup(x => x.SaveCreditChangesAsync(
                It.IsAny<IReadOnlyCollection<LeaveEntitlementWriteSet>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<LeaveEntitlementWriteSet> writes, CancellationToken _) =>
            {
                foreach (var write in writes)
                {
                    if (_balances.All(e => e.Id != write.Entitlement.Id))
                        _balances.Add(write.Entitlement);
                    _audits.AddRange(write.Audits);
                }
            })
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Second_run_same_day_writes_nothing()
    {
        var policy = Resolved(8m, LeaveAmountPeriods.Month, 1);
        Resolve(_ => policy);
        var sut = Create();

        var first = await sut.CreditAsync(_tenantId, [_employeeId], 2026,
            new DateOnly(2026, 3, 1), false, null, default);
        var second = await sut.CreditAsync(_tenantId, [_employeeId], 2026,
            new DateOnly(2026, 3, 1), false, null, default);

        first.Single().HoursWritten.Should().Be(24m);
        second.Single().HoursWritten.Should().Be(0m);
        _audits.Should().ContainSingle(a => a.CoversFromMonth == 1 && a.CoversToMonth == 3 && a.HoursChanged == 24m);
        _balances.Single().TotalHours.Should().Be(24m);
    }

    [Fact]
    public async Task Year_policy_lowered_in_november_writes_negative_remaining_month_delta_without_touching_used()
    {
        var oldPolicy = Resolved(120m, LeaveAmountPeriods.Year, 1);
        var newPolicy = Resolved(60m, LeaveAmountPeriods.Year, 2);
        Resolve(date => date.Month >= 11 ? newPolicy : oldPolicy);
        _balances.Add(new LeaveEntitlement
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
            LeaveTypeId = _leaveTypeId, Year = 2026, TotalHours = 120m, UsedHours = 37m,
            Source = LeaveEntitlementSources.Auto
        });
        _audits.Add(new LeaveBalanceAudit
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
            LeaveTypeId = _leaveTypeId, ChangeType = LeaveBalanceChangeTypes.Accrual,
            HoursChanged = 120m, Year = 2026, CoversFromMonth = 1, CoversToMonth = 12
        });

        var result = await Create().CreditAsync(_tenantId, [_employeeId], 2026,
            new DateOnly(2026, 11, 15), false, null, default);

        result.Single().HoursWritten.Should().Be(-10m);
        _balances.Single().TotalHours.Should().Be(110m);
        _balances.Single().UsedHours.Should().Be(37m);
        _audits.Should().Contain(a => a.HoursChanged == -10m && a.CoversFromMonth == 11 && a.CoversToMonth == 12
            && a.LeavePolicyVersionId == newPolicy.Version.Id);
    }

    [Fact]
    public async Task Existing_year_policy_assigned_in_october_does_not_backfill_january_to_september()
    {
        var policy = Resolved(120m, LeaveAmountPeriods.Year, 1);
        Resolve(date => date.Month >= 10 ? policy : null);

        var result = await Create().CreditAsync(_tenantId, [_employeeId], 2026,
            new DateOnly(2026, 10, 1), false, null, default);

        result.Single().HoursWritten.Should().Be(30m);
        _balances.Single().TotalHours.Should().Be(30m);
        _audits.Should().ContainSingle(a => a.CoversFromMonth == 10 && a.CoversToMonth == 12
            && a.HoursChanged == 30m);
    }

    [Fact]
    public async Task Override_added_mid_october_leaves_october_unchanged_and_credits_november_at_new_rate()
    {
        var company = Resolved(8m, LeaveAmountPeriods.Month, 1);
        var employeeOverride = Resolved(12m, LeaveAmountPeriods.Month, 2);
        Resolve(date => date >= new DateOnly(2026, 10, 15) ? employeeOverride : company);
        _balances.Add(AutoBalance(total: 80m));
        _audits.Add(Accrual(80m, 1, 10, company));
        var sut = Create();

        var october = await sut.CreditAsync(_tenantId, [_employeeId], 2026,
            new DateOnly(2026, 10, 20), false, null, default);
        var november = await sut.CreditAsync(_tenantId, [_employeeId], 2026,
            new DateOnly(2026, 11, 1), false, null, default);

        october.Single().HoursWritten.Should().Be(0m);
        november.Single().HoursWritten.Should().Be(12m);
        _balances.Single().TotalHours.Should().Be(92m);
        _audits.Should().Contain(a => a.CoversFromMonth == 11 && a.CoversToMonth == 11
            && a.HoursChanged == 12m && a.LeavePolicyVersionId == employeeOverride.Version.Id);
    }

    [Fact]
    public async Task Dry_run_reports_credit_without_writing()
    {
        Resolve(_ => Resolved(8m, LeaveAmountPeriods.Month, 1));

        var result = await Create().CreditAsync(_tenantId, [_employeeId], 2026,
            new DateOnly(2026, 3, 1), true, null, default);

        result.Single().HoursWritten.Should().Be(24m);
        _balances.Should().BeEmpty();
        _audits.Should().BeEmpty();
        _entitlements.Verify(x => x.SaveCreditChangesAsync(
            It.IsAny<IReadOnlyCollection<LeaveEntitlementWriteSet>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manual_entitlement_is_skipped()
    {
        Resolve(_ => Resolved(8m, LeaveAmountPeriods.Month, 1));
        _balances.Add(AutoBalance(total: 40m, source: LeaveEntitlementSources.Manual));

        var result = await Create().CreditAsync(_tenantId, [_employeeId], 2026,
            new DateOnly(2026, 3, 1), false, null, default);

        result.Single().SkipReason.Should().Be("Manual entitlement");
        result.Single().HoursWritten.Should().Be(0m);
        _audits.Should().BeEmpty();
    }

    [Fact]
    public async Task Manual_adjustment_is_not_counted_as_policy_credit()
    {
        var policy = Resolved(8m, LeaveAmountPeriods.Month, 1);
        Resolve(_ => policy);
        _balances.Add(AutoBalance(total: 100m));
        _audits.Add(new LeaveBalanceAudit
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
            LeaveTypeId = _leaveTypeId, ChangeType = LeaveBalanceChangeTypes.Adjustment,
            HoursChanged = 100m, Year = 2026
        });

        var result = await Create().CreditAsync(_tenantId, [_employeeId], 2026,
            new DateOnly(2026, 1, 1), false, null, default);

        result.Single().HoursWritten.Should().Be(8m);
        _balances.Single().TotalHours.Should().Be(108m);
        _audits.Should().Contain(a => a.ChangeType == LeaveBalanceChangeTypes.Accrual && a.HoursChanged == 8m);
    }

    [Fact]
    public async Task Close_year_caps_positive_carry_and_forfeits_excess()
    {
        var policy = Resolved(120m, LeaveAmountPeriods.Year, 1);
        policy.Version.CarryForwardCapHours = 40m;
        _balances.Add(AutoBalance(total: 80m, used: 30m));
        var (nextBalances, nextAudits) = ConfigureYearClose(policy);

        await Create().CloseYearAsync(_tenantId, [_employeeId], 2026, null, default);

        nextBalances.Single().CarriedForwardHours.Should().Be(40m);
        nextAudits.Should().Contain(a => a.ChangeType == LeaveBalanceChangeTypes.CarryForward
            && a.HoursChanged == 50m && a.BalanceAfter == 50m);
        nextAudits.Should().Contain(a => a.ChangeType == LeaveBalanceChangeTypes.Forfeiture
            && a.HoursChanged == -10m && a.BalanceAfter == 40m);
    }

    [Fact]
    public async Task Close_year_carries_negative_remaining_in_full_and_is_idempotent()
    {
        var policy = Resolved(120m, LeaveAmountPeriods.Year, 1);
        policy.Version.CarryForwardCapHours = 0m;
        _balances.Add(AutoBalance(total: 20m, used: 35m));
        var (nextBalances, nextAudits) = ConfigureYearClose(policy);
        var sut = Create();

        await sut.CloseYearAsync(_tenantId, [_employeeId], 2026, null, default);
        await sut.CloseYearAsync(_tenantId, [_employeeId], 2026, null, default);

        nextBalances.Single().CarriedForwardHours.Should().Be(-15m);
        nextAudits.Should().ContainSingle(a => a.ChangeType == LeaveBalanceChangeTypes.CarryForward
            && a.HoursChanged == -15m);
        nextAudits.Should().NotContain(a => a.ChangeType == LeaveBalanceChangeTypes.Forfeiture);
    }

    [Fact]
    public async Task Expire_carry_forfeits_on_expiry_date_only_once()
    {
        var policy = Resolved(120m, LeaveAmountPeriods.Year, 1);
        policy.Version.CarryForwardExpiryMonths = 3;
        var balance = AutoBalance(total: 10m);
        balance.Year = 2027;
        balance.CarriedForwardHours = 24m;
        var yearBalances = new List<LeaveEntitlement> { balance };
        var yearAudits = new List<LeaveBalanceAudit>();
        ConfigureYearStorage(2027, yearBalances, yearAudits);
        _resolver.Setup(x => x.ResolveAsync(_tenantId, _employeeId, _leaveTypeId,
                new DateOnly(2027, 1, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);
        var sut = Create();

        await sut.ExpireCarryAsync(_tenantId, 2027, new DateOnly(2027, 3, 31), default);
        balance.CarriedForwardHours.Should().Be(24m);
        await sut.ExpireCarryAsync(_tenantId, 2027, new DateOnly(2027, 4, 1), default);
        await sut.ExpireCarryAsync(_tenantId, 2027, new DateOnly(2027, 4, 1), default);

        balance.CarriedForwardHours.Should().Be(0m);
        yearAudits.Should().ContainSingle(a => a.ChangeType == LeaveBalanceChangeTypes.Forfeiture
            && a.HoursChanged == -24m && a.Reason == "Carried hours expired");
    }

    private LeaveCreditService Create() => new(_employees.Object, _entitlements.Object, _resolver.Object);

    private void Resolve(Func<DateOnly, ResolvedLeavePolicy?> policyForDate)
    {
        _resolver.Setup(x => x.ResolveForEmployeesAsync(
                _tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> _, DateOnly date, CancellationToken _) =>
            {
                var policy = policyForDate(date);
                return new Dictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>>
                {
                    [_employeeId] = policy is null ? [] : [policy]
                };
            });
    }

    private LeaveEntitlement AutoBalance(
        decimal total,
        decimal used = 0m,
        string source = LeaveEntitlementSources.Auto) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
        LeaveTypeId = _leaveTypeId, Year = 2026, TotalHours = total, UsedHours = used, Source = source
    };

    private LeaveBalanceAudit Accrual(
        decimal hours,
        int fromMonth,
        int toMonth,
        ResolvedLeavePolicy policy) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
        LeaveTypeId = _leaveTypeId, ChangeType = LeaveBalanceChangeTypes.Accrual,
        HoursChanged = hours, Year = 2026, CoversFromMonth = fromMonth, CoversToMonth = toMonth,
        LeavePolicyId = policy.PolicyId, LeavePolicyVersionId = policy.Version.Id
    };

    private (List<LeaveEntitlement> Balances, List<LeaveBalanceAudit> Audits) ConfigureYearClose(
        ResolvedLeavePolicy policy)
    {
        var nextBalances = new List<LeaveEntitlement>();
        var nextAudits = new List<LeaveBalanceAudit>();
        ConfigureYearStorage(2027, nextBalances, nextAudits);
        _resolver.Setup(x => x.ResolveAsync(_tenantId, _employeeId, _leaveTypeId,
                new DateOnly(2026, 12, 31), It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);
        return (nextBalances, nextAudits);
    }

    private void ConfigureYearStorage(
        int year,
        List<LeaveEntitlement> balances,
        List<LeaveBalanceAudit> audits)
    {
        _entitlements.Setup(x => x.ListTrackedExistingAsync(
                _tenantId, year, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(balances);
        _entitlements.Setup(x => x.ListAuditsAsync(
                _tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(audits);
        _entitlements.Setup(x => x.SaveCreditChangesAsync(
                It.IsAny<IReadOnlyCollection<LeaveEntitlementWriteSet>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<LeaveEntitlementWriteSet> writes, CancellationToken _) =>
            {
                foreach (var write in writes)
                {
                    if (write.Entitlement.Year == year && balances.All(e => e.Id != write.Entitlement.Id))
                        balances.Add(write.Entitlement);
                    if (write.Entitlement.Year == year)
                        audits.AddRange(write.Audits);
                }
            })
            .Returns(Task.CompletedTask);
    }

    private ResolvedLeavePolicy Resolved(decimal amount, string period, int number)
    {
        var policyId = Guid.NewGuid();
        var version = new LeavePolicyVersion
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LeavePolicyId = policyId,
            VersionNumber = number, EffectiveFrom = number == 1 ? new DateOnly(2026, 1, 1) : new DateOnly(2026, 11, 1),
            AmountHours = amount, AmountPeriod = period, EligibleFrom = LeaveEligibility.Immediately
        };
        return new ResolvedLeavePolicy(_leaveTypeId, policyId, $"Policy v{number}", version, [],
            LeavePolicyTargetKinds.LegalEntity, Guid.NewGuid(), "Company");
    }
}
