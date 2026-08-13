namespace ONEVO.Application.Features.DevPlatform.Billing.Helpers;

public static class InvoiceStatusRules
{
    public static readonly string[] ValidStatuses = ["draft", "open", "paid", "void"];

    public static bool IsValid(string status) =>
        ValidStatuses.Contains(status, StringComparer.Ordinal);

    public static bool CanMarkPaid(string status) =>
        status is "draft" or "open";

    public static bool CanVoid(string status) =>
        status is "draft" or "open";
}
