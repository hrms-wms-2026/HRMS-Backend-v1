using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

/// <summary>Parses the shared `compare` query value of comparison-capable Overview widgets.</summary>
public static class EmployeeOverviewCompare
{
    public static Result<bool> Parse(string? compare)
    {
        if (string.IsNullOrWhiteSpace(compare) || compare.Equals("none", StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Success(false);
        if (compare.Equals("previous", StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Success(true);
        return Result<bool>.Failure("compare must be 'previous' or 'none'.");
    }
}
