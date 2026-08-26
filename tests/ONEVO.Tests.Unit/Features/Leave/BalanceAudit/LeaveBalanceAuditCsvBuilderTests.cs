using System.Text;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;
using ONEVO.Application.Features.Leave.BalanceAudit.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.BalanceAudit;

public class LeaveBalanceAuditCsvBuilderTests
{
    [Fact]
    public void Build_ProducesHeaderAndOneRowPerAudit()
    {
        var rows = new List<LeaveBalanceAuditResponse>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "EMP001", "Priya Kumar", Guid.NewGuid(), "Annual Leave", "ANNUAL",
                "deduction", -3m, 7m, "Leave approved", Guid.NewGuid(), new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero))
        };

        var file = LeaveBalanceAuditCsvBuilder.Build(rows);

        var text = Encoding.UTF8.GetString(file.Content);
        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("Employee Number,Employee Name,Leave Type,Change Type,Days Changed,Balance After,Reason,Date", lines[0]);
        Assert.Contains("EMP001,Priya Kumar,Annual Leave,deduction,-3,7,Leave approved,2026-04-10", lines[1]);
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal("leave-balance-audit.csv", file.FileName);
    }
}
