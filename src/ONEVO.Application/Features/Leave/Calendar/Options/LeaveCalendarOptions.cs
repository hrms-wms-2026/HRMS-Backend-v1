namespace ONEVO.Application.Features.Leave.Calendar.Options;

public sealed class LeaveCalendarOptions
{
    public const string SectionName = "Leave:Calendar";

    public bool DefaultIncludeTentativeBlocks { get; init; }

    public Dictionary<string, string> TypeCategoryColors { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? ColorFor(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return null;

        return TypeCategoryColors.TryGetValue(category.Trim(), out var color)
            ? color
            : null;
    }

    public static bool AreColorsValid(IReadOnlyDictionary<string, string>? colors)
        => colors is null || colors.Values.All(IsValidHexColor);

    public static bool IsValidHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.Length != 7 || text[0] != '#')
            return false;

        return text.Skip(1).All(c =>
            c is >= '0' and <= '9'
            || c is >= 'a' and <= 'f'
            || c is >= 'A' and <= 'F');
    }
}
