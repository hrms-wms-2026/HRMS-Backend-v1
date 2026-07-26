using System.Text.RegularExpressions;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Helpers;

/// <summary>
/// Canonical validation for the operator-set payment_gateway_configs.gateway_key.
/// Keys are non-secret, URL-safe, lowercase identifiers used by webhook routes.
/// </summary>
public static partial class PaymentGatewayKeyRules
{
    public const int MaxLength = 80;

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex GatewayKeyPattern();

    public static bool IsValid(string? gatewayKey) =>
        !string.IsNullOrWhiteSpace(gatewayKey) &&
        gatewayKey.Length <= MaxLength &&
        GatewayKeyPattern().IsMatch(gatewayKey);
}
