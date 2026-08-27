using System.Text;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.BalanceAudit.Helpers;

public static class LeaveBalanceAuditCsvBuilder
{
    public static LeaveExportFile Build(IReadOnlyList<LeaveBalanceAuditResponse> rows)
    {
        var sb = new StringBuilder();
        sb.Append("Employee Number,Employee Name,Leave Type,Change Type,Days Changed,Balance After,Reason,Date\n");

        foreach (var row in rows)
        {
            sb.Append(Csv(row.EmployeeNumber)).Append(',')
              .Append(Csv(row.EmployeeName)).Append(',')
              .Append(Csv(row.LeaveTypeName)).Append(',')
              .Append(Csv(row.ChangeType)).Append(',')
              .Append(row.DaysChanged).Append(',')
              .Append(row.BalanceAfter).Append(',')
              .Append(Csv(row.Reason ?? "")).Append(',')
              .Append(row.CreatedAt.ToString("yyyy-MM-dd"))
              .Append('\n');
        }

        return new LeaveExportFile(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "leave-balance-audit.csv");
    }

    // Quotes a field only when it contains a comma, quote, or newline - minimal CSV escaping,
    // extended beyond GetBulkOnboardingTemplateQueryHandler.BuildCsv()'s precedent (which never
    // needed it, its fields are controlled labels) since Reason/EmployeeName are free text.
    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
