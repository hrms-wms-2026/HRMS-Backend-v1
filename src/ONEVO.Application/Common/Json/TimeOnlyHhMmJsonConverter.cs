using System.Text.Json;
using System.Text.Json.Serialization;

namespace ONEVO.Application.Common.Json;

// Stable "HH:mm" 24-hour wire format for TimeOnly, e.g. "09:00" - the
// built-in System.Text.Json TimeOnly converter emits seconds
// ("09:00:00"), which General Settings' workStartTime/workEndTime
// contract deliberately avoids. Registered globally in Program.cs so it
// also applies to Nullable<TimeOnly> properties.
public class TimeOnlyHhMmJsonConverter : JsonConverter<TimeOnly>
{
    private const string Format = "HH:mm";

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!TimeOnly.TryParseExact(value, Format, out var result))
            throw new JsonException($"Invalid time format. Expected \"{Format}\", got \"{value}\".");

        return result;
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Format));
    }
}
