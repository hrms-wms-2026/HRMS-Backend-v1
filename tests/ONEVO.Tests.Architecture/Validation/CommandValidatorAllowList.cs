namespace ONEVO.Tests.Architecture.Validation;

/// <summary>
/// Commands that take string input but do not have a validator yet. Task 19b emptied it: every string-carrying
/// command now has a validator. Keep it empty. A new command that takes strings needs a validator
/// (see ONEVO.Application.Common.Validation.ValidationRuleExtensions); never add it here instead.
/// CommandValidatorCoverageArchitectureTests fails while a listed command has a validator.
/// </summary>
internal static class CommandValidatorAllowList
{
    public static readonly string[] Commands =
    [
    ];
}
