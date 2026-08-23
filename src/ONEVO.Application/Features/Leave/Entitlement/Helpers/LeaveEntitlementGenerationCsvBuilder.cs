using System.Text;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Entitlement.Helpers;

public static class LeaveEntitlementGenerationCsvBuilder
{
    public static LeaveExportFile Build(LeaveEntitlementGenerationPreviewResponse preview)
    {
        var sb = new StringBuilder();
        sb.Append("Employee Number,Employee Name,Leave Type,Total Days,Carried Forward,Remaining,Status,Reason\n");

        foreach (var line in preview.Lines)
        {
            sb.Append(line.EmployeeNumber).Append(',')
              .Append(Csv(line.EmployeeName)).Append(',')
              .Append(Csv(line.LeaveTypeName)).Append(',')
              .Append(line.TotalDays).Append(',')
              .Append(line.CarriedForwardDays).Append(',')
              .Append(line.RemainingDays).Append(',')
              .Append("Will be created").Append(',')
              .Append(Csv(line.Warning ?? "")).Append('\n');
        }

        foreach (var skip in preview.Skipped)
        {
            sb.Append("").Append(',')
              .Append(Csv(skip.EmployeeName ?? "")).Append(',')
              .Append("").Append(',').Append("").Append(',').Append("").Append(',').Append("")
              .Append(',').Append("Skipped").Append(',')
              .Append(Csv(skip.Reason)).Append('\n');
        }

        return new LeaveExportFile(
            Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"leave-entitlement-generation-{preview.Year}.csv");
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
