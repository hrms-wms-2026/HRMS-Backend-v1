using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Application.Features.Leave.Entitlement.Mappers;

public static class LeaveEntitlementMapper
{
    public static decimal Remaining(
        decimal totalDays,
        decimal carriedForwardDays,
        decimal usedDays,
        decimal pendingDays) =>
        totalDays + carriedForwardDays - usedDays - pendingDays;

    public static decimal Remaining(LeaveEntitlement entitlement) =>
        Remaining(entitlement.TotalDays, entitlement.CarriedForwardDays, entitlement.UsedDays, entitlement.PendingDays);

    public static decimal EffectiveCarry(decimal carriedForwardDays, DateOnly? expiresOn, DateOnly asOfDate) =>
        expiresOn is { } expiry && asOfDate >= expiry ? 0m : carriedForwardDays;

    public static LeaveEntitlementResponse ToResponse(LeaveEntitlementRow row, string? warning, DateOnly asOfDate, DateOnly? carryExpiresOn)
    {
        var entitlement = row.Entitlement;
        var carry = EffectiveCarry(entitlement.CarriedForwardDays, carryExpiresOn, asOfDate);
        var remaining = Remaining(entitlement.TotalDays, carry, entitlement.UsedDays, entitlement.PendingDays);

        return new LeaveEntitlementResponse(
            entitlement.Id,
            entitlement.EmployeeId,
            row.EmployeeNumber,
            row.EmployeeName,
            entitlement.LeaveTypeId,
            row.LeaveTypeName,
            row.LeaveTypeCode,
            entitlement.Year,
            entitlement.TotalDays,
            entitlement.CarriedForwardDays,
            entitlement.UsedDays,
            entitlement.PendingDays,
            remaining,
            entitlement.Source,
            entitlement.ManualReason,
            remaining < 0m,
            warning,
            entitlement.CreatedAt,
            entitlement.UpdatedAt);
    }

    public static LeaveBalanceResponse ToBalance(LeaveEntitlementRow row, DateOnly asOfDate, DateOnly? carryExpiresOn)
    {
        var entitlement = row.Entitlement;
        var carry = EffectiveCarry(entitlement.CarriedForwardDays, carryExpiresOn, asOfDate);
        var remaining = Remaining(entitlement.TotalDays, carry, entitlement.UsedDays, entitlement.PendingDays);

        return new LeaveBalanceResponse(
            entitlement.EmployeeId,
            row.EmployeeNumber,
            row.EmployeeName,
            row.DepartmentId,
            row.DepartmentName,
            row.LegalEntityId,
            row.LegalEntityName,
            entitlement.LeaveTypeId,
            row.LeaveTypeName,
            row.LeaveTypeCode,
            entitlement.Year,
            entitlement.TotalDays + carry,
            entitlement.TotalDays,
            carry,
            entitlement.UsedDays,
            entitlement.PendingDays,
            remaining,
            remaining < 0m,
            carryExpiresOn);
    }

    public static string EmployeeName(string firstName, string lastName) =>
        $"{firstName} {lastName}".Trim();
}
