namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

public static class ColumnMappingSuggester
{
    private static readonly Dictionary<string, string[]> FieldAliases = new()
    {
        ["firstName"] = ["first name", "firstname", "given name"],
        ["lastName"] = ["last name", "lastname", "surname", "family name"],
        ["workEmail"] = ["work email", "email", "email address"],
        ["startDate"] = ["start date", "startdate", "joining date", "hire date"],
        ["employmentType"] = ["employment type", "employmenttype", "employment"],
        ["workMode"] = ["work mode", "workmode"],
        ["department"] = ["department", "dept"],
        ["position"] = ["position", "job title", "title", "role"],
        ["checklistTemplate"] = ["checklist template", "template"],
        ["employeeNumber"] = ["employee number", "employee no", "emp id", "employee id"],
        ["reportingManager"] = ["reporting manager", "manager", "reports to", "team lead"],
    };

    public static IReadOnlyDictionary<string, string?> Suggest(IReadOnlyList<string> csvHeaders)
    {
        var result = new Dictionary<string, string?>();
        foreach (var (systemField, aliases) in FieldAliases)
        {
            var match = csvHeaders.FirstOrDefault(h =>
                aliases.Any(alias => string.Equals(
                    Normalize(h), Normalize(alias), StringComparison.OrdinalIgnoreCase)));
            result[systemField] = match;
        }
        return result;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
