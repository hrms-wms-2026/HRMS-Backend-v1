using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public class CsvBatchParserTests
{
    [Fact]
    public void Parse_SimpleCsv_ReturnsHeadersAndRows()
    {
        var csv = "First Name,Last Name,Work Email\nJane,Doe,jane@acme.com\nJohn,Roe,john@acme.com\n";

        var result = CsvBatchParser.Parse(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "First Name", "Last Name", "Work Email" }, result.Value!.Headers);
        Assert.Equal(2, result.Value.Rows.Count);
        Assert.Equal("jane@acme.com", result.Value.Rows[0]["Work Email"]);
    }

    [Fact]
    public void Parse_QuotedFieldWithEmbeddedComma_ParsesAsOneValue()
    {
        var csv = "Name,Notes\n\"Doe, Jane\",\"Started Q1, remote\"\n";

        var result = CsvBatchParser.Parse(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal("Doe, Jane", result.Value!.Rows[0]["Name"]);
        Assert.Equal("Started Q1, remote", result.Value.Rows[0]["Notes"]);
    }

    [Fact]
    public void Parse_MoreThanMaxRows_ReturnsFailure()
    {
        var header = "Email\n";
        var rows = string.Concat(Enumerable.Range(0, CsvBatchParser.MaxRows + 1).Select(i => $"user{i}@acme.com\n"));

        var result = CsvBatchParser.Parse(header + rows);

        Assert.False(result.IsSuccess);
        Assert.Contains("200", result.Error);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsFailure()
    {
        var result = CsvBatchParser.Parse("");
        Assert.False(result.IsSuccess);
    }
}
