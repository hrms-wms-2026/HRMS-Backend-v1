using FluentValidation;
using ONEVO.Application.Features.TimeAttendance.Models;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Validation;

public static class ClockInPolicyValidationRules
{
    public static readonly string[] ScopeTypes =
    [
        ClockInPolicy.ScopeFullCompany,
        ClockInPolicy.ScopeDepartment,
        ClockInPolicy.ScopePosition,
        ClockInPolicy.ScopeEmployee
    ];

    public static readonly string[] HybridSourceRules =
    [
        ClockInPolicy.HybridSourceOnsite,
        ClockInPolicy.HybridSourceRemote,
        ClockInPolicy.HybridSourceEmployeeChoice
    ];

    public static readonly string[] FieldPhotoRequirements =
    [
        ClockInPolicy.FieldPhotoOff,
        ClockInPolicy.FieldPhotoOptional,
        ClockInPolicy.FieldPhotoRequired
    ];

    public static void ApplyScopeRules<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, ClockInPolicyScopeInput>> scopeSelector)
    {
        validator.RuleFor(scopeSelector)
            .Must(s => ScopeTypes.Contains(s.Type))
            .WithMessage("Scope type must be one of: full_company, department, position, employee.");

        validator.RuleFor(scopeSelector)
            .Must(s =>
                s.Type != ClockInPolicy.ScopeFullCompany
                || IsEmpty(s.DepartmentIds) && IsEmpty(s.PositionIds) && IsEmpty(s.EmployeeIds))
            .WithMessage("Full company scope must not include department, position, or employee IDs.");

        validator.RuleFor(scopeSelector)
            .Must(s => s.Type != ClockInPolicy.ScopeDepartment || !IsEmpty(s.DepartmentIds))
            .WithMessage("Department scope requires at least one department ID.");

        validator.RuleFor(scopeSelector)
            .Must(s => s.Type != ClockInPolicy.ScopePosition || !IsEmpty(s.PositionIds))
            .WithMessage("Position scope requires at least one position ID.");

        validator.RuleFor(scopeSelector)
            .Must(s => s.Type != ClockInPolicy.ScopeEmployee || !IsEmpty(s.EmployeeIds))
            .WithMessage("Employee scope requires at least one employee ID.");
    }

    public static void ApplyLateRuleItemRules(InlineValidator<LateDeductionRuleInput> rule)
    {
        rule.RuleFor(r => r.LateArrivalMinute)
            .GreaterThan(0)
            .WithMessage("Late arrival minute must be positive.");

        rule.RuleFor(r => r.Multiplier)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Multiplier must be greater than or equal to zero.");

        rule.RuleFor(r => r.TimeOffTypeId)
            .NotEmpty()
            .WithMessage("Time off type ID is required.");
    }

    public static bool IsEmpty(IReadOnlyList<Guid>? ids)
        => ids is null || ids.Count == 0;
}
