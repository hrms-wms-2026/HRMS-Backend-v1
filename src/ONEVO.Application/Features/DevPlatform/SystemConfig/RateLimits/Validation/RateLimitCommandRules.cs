using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

/// <summary>What every "set a rate limit" command carries.</summary>
public interface ISetRateLimitCommand
{
    RateLimitName LimitName { get; }
    int PerMinute { get; }
    int? Burst { get; }
    string Reason { get; }
}

/// <summary>What every "remove a rate limit" command carries.</summary>
public interface IRemoveRateLimitCommand
{
    RateLimitName LimitName { get; }
    string Reason { get; }
}

/// <summary>
/// The shared checks of task 47: values inside <see cref="ONEVO.Domain.Features.SharedPlatform.RateLimiting.RateLimitBounds"/>,
/// burst only and always for token-bucket limits, a per-minute value that divides exactly into
/// tokens per refill period, and a plain-text reason of 10 to 500 characters.
/// </summary>
public static partial class RateLimitCommandRules
{
    public const int ReasonMinLength = 10;
    public const int ReasonMaxLength = 500;

    public static void AddSetRules<T>(AbstractValidator<T> validator, IOptionsMonitor<RateLimitSettings> appSettings, bool perTenantOnly)
        where T : ISetRateLimitCommand
    {
        validator.RuleFor(x => x.LimitName).IsInEnum();
        if (perTenantOnly)
            AddPerTenantRule(validator, x => x.LimitName);

        validator.RuleFor(x => x.PerMinute)
            .Must((command, perMinute) => InRange(perMinute, command.LimitName.Bounds().MinPerMinute, command.LimitName.Bounds().MaxPerMinute))
            .WithMessage(command => $"perMinute must be between {command.LimitName.Bounds().MinPerMinute} and "
                + $"{command.LimitName.Bounds().MaxPerMinute} for {command.LimitName}.")
            .When(x => Enum.IsDefined(x.LimitName));

        validator.RuleFor(x => x.PerMinute)
            .Must((command, perMinute) => DividesExactly(perMinute, RateLimitResolver.ReplenishmentPeriodSeconds(appSettings.CurrentValue, command.LimitName)))
            .WithMessage(command => $"perMinute must divide exactly into tokens per "
                + $"{RateLimitResolver.ReplenishmentPeriodSeconds(appSettings.CurrentValue, command.LimitName)}-second period "
                + "(a multiple of 60 with the default 1-second period).")
            .When(x => Enum.IsDefined(x.LimitName) && !x.LimitName.IsSlidingWindow());

        validator.RuleFor(x => x.Burst)
            .Null()
            .WithMessage(command => $"{command.LimitName} is a sliding window and has no burst.")
            .When(x => Enum.IsDefined(x.LimitName) && x.LimitName.IsSlidingWindow());

        validator.RuleFor(x => x.Burst)
            .NotNull()
            .WithMessage(command => $"burst is required for {command.LimitName}.")
            .Must((command, burst) => burst is int value
                && InRange(value, command.LimitName.Bounds().MinBurst!.Value, command.LimitName.Bounds().MaxBurst!.Value))
            .WithMessage(command => $"burst must be between {command.LimitName.Bounds().MinBurst} and "
                + $"{command.LimitName.Bounds().MaxBurst} for {command.LimitName}.")
            .When(x => Enum.IsDefined(x.LimitName) && !x.LimitName.IsSlidingWindow());

        AddReasonRules(validator, x => x.Reason);
    }

    public static void AddRemoveRules<T>(AbstractValidator<T> validator, bool perTenantOnly)
        where T : IRemoveRateLimitCommand
    {
        validator.RuleFor(x => x.LimitName).IsInEnum();
        if (perTenantOnly)
            AddPerTenantRule(validator, x => x.LimitName);
        AddReasonRules(validator, x => x.Reason);
    }

    /// <summary>Anonymous, AdminAnonymous and AdminSignedIn are platform-wide: the limiter reads them from GetSettings(null).</summary>
    private static void AddPerTenantRule<T>(AbstractValidator<T> validator, System.Linq.Expressions.Expression<Func<T, RateLimitName>> limit) =>
        validator.RuleFor(limit)
            .Must(name => !Enum.IsDefined(name) || name.IsPerTenant())
            .WithMessage("Only SignedIn, TrayDevice and TenantCeiling can be set on a plan or tenant.");

    private static void AddReasonRules<T>(AbstractValidator<T> validator, System.Linq.Expressions.Expression<Func<T, string>> reason) =>
        validator.RuleFor(reason)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(value => value.Trim().Length is >= ReasonMinLength and <= ReasonMaxLength)
            .WithMessage($"reason must be {ReasonMinLength} to {ReasonMaxLength} characters.")
            .Must(IsPlainText)
            .WithMessage("reason must be plain text.");

    public static bool IsPlainText(string value) =>
        !RichTextRules.HasActiveContent(value) && !MarkupTag().IsMatch(value);

    private static bool InRange(int value, int min, int max) => value >= min && value <= max;

    private static bool DividesExactly(int perMinute, int? periodSeconds) =>
        periodSeconds is not int period || period <= 0 || (long)perMinute * period % 60 == 0;

    [GeneratedRegex(@"<\s*/?\s*[a-zA-Z!]")]
    private static partial Regex MarkupTag();
}
