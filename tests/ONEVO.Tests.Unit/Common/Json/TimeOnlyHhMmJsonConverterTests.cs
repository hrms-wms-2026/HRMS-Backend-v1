using System.Text.Json;
using FluentAssertions;
using ONEVO.Application.Common.Json;
using Xunit;

namespace ONEVO.Tests.Unit.Common.Json;

public class TimeOnlyHhMmJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new TimeOnlyHhMmJsonConverter() }
    };

    private record Wrapper(TimeOnly? Value);

    [Fact]
    public void Write_NineAm_ProducesHhMmString()
    {
        var json = JsonSerializer.Serialize(new Wrapper(new TimeOnly(9, 0)), Options);
        json.Should().Be("""{"Value":"09:00"}""");
    }

    [Fact]
    public void Write_FivePm30_ProducesHhMmString()
    {
        var json = JsonSerializer.Serialize(new Wrapper(new TimeOnly(17, 30)), Options);
        json.Should().Be("""{"Value":"17:30"}""");
    }

    [Fact]
    public void Write_Null_ProducesJsonNull()
    {
        var json = JsonSerializer.Serialize(new Wrapper(null), Options);
        json.Should().Be("""{"Value":null}""");
    }

    [Fact]
    public void Read_HhMmString_ParsesToTimeOnly()
    {
        var wrapper = JsonSerializer.Deserialize<Wrapper>("""{"Value":"09:00"}""", Options);
        wrapper!.Value.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public void Read_JsonNull_ParsesToNull()
    {
        var wrapper = JsonSerializer.Deserialize<Wrapper>("""{"Value":null}""", Options);
        wrapper!.Value.Should().BeNull();
    }

    [Fact]
    public void Read_InvalidFormat_Throws()
    {
        Action act = () => JsonSerializer.Deserialize<Wrapper>("""{"Value":"not-a-time"}""", Options);
        act.Should().Throw<JsonException>();
    }
}
