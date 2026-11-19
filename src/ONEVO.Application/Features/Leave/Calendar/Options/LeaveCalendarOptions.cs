namespace ONEVO.Application.Features.Leave.Calendar.Options;

public sealed class LeaveCalendarOptions
{
    public const string SectionName = "Leave:Calendar";

    public bool DefaultIncludeTentativeBlocks { get; init; }

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
