using System.Text;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class LeaveEntitlementGenerationCsvBuilderTests
{
    [Fact]
    public void Build_ListsCreatedLinesThenSkippedRows()
    {
        var preview = new LeaveEntitlementGenerationPreviewResponse(
            2027, 1, 1,
            Lines: [new LeaveEntitlementGenerationLineResponse(
                Guid.NewGuid(), "EMP001", "Priya Kumar", Guid.NewGuid(), "Annual Leave",
                20m, 5m, 25m, false, 0m, null, null)],
            Skipped: [new LeaveEntitlementGenerationSkipResponse(Guid.NewGuid(), "Alex Roy", "No leave policy assigned to their legal entity")]);

        var file = LeaveEntitlementGenerationCsvBuilder.Build(preview);

        var text = Encoding.UTF8.GetString(file.Content);
        Assert.Contains("EMP001,Priya Kumar,Annual Leave,20,5,25", text);
        Assert.Contains("Alex Roy,,,,,Skipped,No leave policy assigned to their legal entity", text);
        Assert.Equal("leave-entitlement-generation-2027.csv", file.FileName);
    }
}
