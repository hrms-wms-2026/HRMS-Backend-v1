using Microsoft.Extensions.Options;

namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// Startup validation of <see cref="EmailRateOptions"/>; the message names the configuration key to fix.
/// <para>
/// A negative limit is rejected rather than clamped: a negative value would otherwise mean "unlimited",
/// which is the opposite of what a typo in the configuration was meant to say.
/// </para>
/// </summary>
public sealed class EmailRateOptionsValidator : IValidateOptions<EmailRateOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailRateOptions options)
    {
        if (options.MaxPerSecond < 0)
        {
            return ValidateOptionsResult.Fail(
                $"{EmailRateOptions.SectionName}:{nameof(EmailRateOptions.MaxPerSecond)} must be 0 (unlimited) or positive, but it was {options.MaxPerSecond}.");
        }

        return ValidateOptionsResult.Success;
    }
}
