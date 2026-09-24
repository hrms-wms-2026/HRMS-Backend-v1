using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Application.Features.Leave.Entitlement.Mappers;

public static class LeaveEntitlementMapper
{
    public static decimal Remaining(
        decimal totalHours,
        decimal carriedForwardHours,
        decimal usedHours,
        decimal pendingHours) =>
        totalHours + carriedForwardHours - usedHours - pendingHours;

    public static decimal Remaining(LeaveEntitlement entitlement) =>
        Remaining(entitlement.TotalHours, entitlement.CarriedForwardHours, entitlement.UsedHours, entitlement.PendingHours);

    public static decimal EffectiveCarry(decimal carriedForwardHours, DateOnly? expiresOn, DateOnly asOfDate) =>
        expiresOn is { } expiry && asOfDate >= expiry ? 0m : carriedForwardHours;

    public static LeaveEntitlementResponse ToResponse(LeaveEntitlementRow row, string? warning, DateOnly asOfDate, DateOnly? carryExpiresOn)
    {
        var entitlement = row.Entitlement;
        var carry = EffectiveCarry(entitlement.CarriedForwardHours, carryExpiresOn, asOfDate);
        var remaining = Remaining(entitlement.TotalHours, carry, entitlement.UsedHours, entitlement.PendingHours);

        return new LeaveEntitlementResponse(
            entitlement.Id,
            entitlement.EmployeeId,
            row.EmployeeNumber,
            row.EmployeeName,
            entitlement.LeaveTypeId,
            row.LeaveTypeName,
            row.LeaveTypeCode,
            entitlement.Year,
            entitlement.TotalHours,
            entitlement.CarriedForwardHours,
            entitlement.UsedHours,
            entitlement.PendingHours,
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
        var carry = EffectiveCarry(entitlement.CarriedForwardHours, carryExpiresOn, asOfDate);
        var remaining = Remaining(entitlement.TotalHours, carry, entitlement.UsedHours, entitlement.PendingHours);

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
            entitlement.TotalHours + carry,
            entitlement.TotalHours,
            carry,
            entitlement.UsedHours,
            entitlement.PendingHours,
            remaining,
            remaining < 0m,
            carryExpiresOn);
    }

    public static string EmployeeName(string firstName, string lastName) =>
        $"{firstName} {lastName}".Trim();
}
