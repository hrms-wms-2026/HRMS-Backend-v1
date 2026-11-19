using System.Linq.Expressions;
using FluentValidation;
using ONEVO.Application.Common.Helpers;

namespace ONEVO.Application.Common.Validation;

/// <summary>
/// Shared FluentValidation rules for command input (task 19). Validators run before the handler, so these are the
/// server-side security bounds: every string has a maximum length that matches its column, every id list has a
/// maximum count, every date range has a maximum span, and user-written rich text is checked with
/// <see cref="RichTextRules.HasActiveContent"/>. Handlers keep their own business rules and friendlier messages.
/// </summary>
public static class ValidationRuleExtensions
{
    /// <summary>A name or other short plain-text field: at most <paramref name="maxLength"/> characters. When
    /// <paramref name="required"/>, it must also hold something other than whitespace.</summary>
    public static IRuleBuilderOptions<T, string?> ValidName<T>(
        this IRuleBuilder<T, string?> rule, int maxLength, bool required = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        if (required)
            rule = rule.Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage("'{PropertyName}' is required.");
        return rule.Must(value => value is null || value.Length <= maxLength)
            .WithMessage($"'{{PropertyName}}' must be {maxLength} characters or fewer.");
    }

    /// <summary>User-written rich text (HTML): at most <paramref name="maxLength"/> characters and no active
    /// content (scripts, embedded frames, inline event handlers, script URLs). Emptiness is left to the handler,
    /// because some fields may be empty when files are attached.</summary>
    public static IRuleBuilderOptions<T, string?> ValidRichText<T>(this IRuleBuilder<T, string?> rule, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        return rule
            .Must(value => value is null || value.Length <= maxLength)
            .WithMessage($"'{{PropertyName}}' must be {maxLength} characters or fewer.")
            .Must(value => !RichTextRules.HasActiveContent(value))
            .WithMessage(RichTextRules.ActiveContentError);
    }

    /// <summary>A list of ids from the request: at most <paramref name="maxCount"/> entries. A null list passes
    /// (handlers treat it as empty, and drop empty or repeated ids themselves).</summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>?> ValidIdList<T>(
        this IRuleBuilder<T, IReadOnlyList<Guid>?> rule, int maxCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        return rule
            .Must(ids => ids is null || ids.Count <= maxCount)
            .WithMessage($"'{{PropertyName}}' can hold at most {maxCount} entries.");
    }

    /// <summary>A list of short strings from the request (emails, codes, keys): at most <paramref name="maxCount"/>
    /// entries, each at most <paramref name="maxItemLength"/> characters. A null list and null entries pass; the
    /// handlers normalize and check the values themselves. Task 19b.</summary>
    public static IRuleBuilderOptions<T, TList> ValidStringList<T, TList>(
        this IRuleBuilder<T, TList> rule, int maxCount, int maxItemLength)
        where TList : IEnumerable<string?>?
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItemLength);
        return rule
            .Must(items => items is null || items.Count() <= maxCount)
            .WithMessage($"'{{PropertyName}}' can hold at most {maxCount} entries.")
            .Must(items => items is null || items.All(item => item is null || item.Length <= maxItemLength))
            .WithMessage($"Each entry in '{{PropertyName}}' must be {maxItemLength} characters or fewer.");
    }

    /// <summary>A date-time range on the whole command: when both ends are set, the end must not be before the start
    /// and the range must not be longer than <paramref name="maxSpan"/>. Use with <c>RuleFor(x =&gt; x)</c>; the
    /// failure is reported against the end property.</summary>
    public static IRuleBuilderOptionsConditions<T, T> ValidDateRange<T>(
        this IRuleBuilder<T, T> rule,
        Expression<Func<T, DateTimeOffset?>> start,
        Expression<Func<T, DateTimeOffset?>> end,
        TimeSpan maxSpan)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxSpan, TimeSpan.Zero);
        var startOf = start.Compile();
        var endOf = end.Compile();
        var startName = MemberName(start);
        var endName = MemberName(end);
        return rule.Custom((command, context) =>
        {
            if (command is null || startOf(command) is not { } from || endOf(command) is not { } to)
                return;
            if (to < from)
                context.AddFailure(endName, $"'{endName}' must not be before '{startName}'.");
            else if (to - from > maxSpan)
                context.AddFailure(endName, $"The range from '{startName}' to '{endName}' must not be longer than {Describe(maxSpan)}.");
        });
    }

    /// <summary>A date range on the whole command (DateOnly ends). See the DateTimeOffset overload.</summary>
    public static IRuleBuilderOptionsConditions<T, T> ValidDateRange<T>(
        this IRuleBuilder<T, T> rule,
        Expression<Func<T, DateOnly?>> start,
        Expression<Func<T, DateOnly?>> end,
        int maxDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);
        var startOf = start.Compile();
        var endOf = end.Compile();
        var startName = MemberName(start);
        var endName = MemberName(end);
        return rule.Custom((command, context) =>
        {
            if (command is null || startOf(command) is not { } from || endOf(command) is not { } to)
                return;
            if (to < from)
                context.AddFailure(endName, $"'{endName}' must not be before '{startName}'.");
            else if (to.DayNumber - from.DayNumber > maxDays)
                context.AddFailure(endName, $"The range from '{startName}' to '{endName}' must not be longer than {maxDays} days.");
        });
    }

    /// <summary>Paging input: the page number starts at 1 and the page size is between 1 and
    /// <paramref name="maxPageSize"/>.</summary>
    public static void ValidPageRequest<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> pageNumber,
        Expression<Func<T, int>> pageSize,
        int maxPageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPageSize);
        validator.RuleFor(pageNumber).GreaterThanOrEqualTo(1);
        validator.RuleFor(pageSize).InclusiveBetween(1, maxPageSize);
    }

    private static string MemberName(LambdaExpression expression)
    {
        var body = expression.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : expression.Body;
        return body is MemberExpression member ? member.Member.Name : expression.ToString();
    }

    private static string Describe(TimeSpan span) =>
        span.TotalDays >= 1 && span.TotalDays == Math.Floor(span.TotalDays) ? $"{span.TotalDays:0} days"
        : span.TotalHours == Math.Floor(span.TotalHours) ? $"{span.TotalHours:0} hours"
        : span.ToString();
}
