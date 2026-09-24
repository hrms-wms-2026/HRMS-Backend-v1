namespace ONEVO.Application.Features.DevPlatform.Billing.Helpers;

public static class InvoiceNumberGenerator
{
    public static string Generate(DateTimeOffset now) =>
        $"INV-{now:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";
}
