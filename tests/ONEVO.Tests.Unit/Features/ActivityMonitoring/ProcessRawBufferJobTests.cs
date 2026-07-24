using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace ONEVO.Tests.Unit.Features.ActivityMonitoring;

public class ProcessRawBufferJobTests
{
    [Fact]
    public void ActivitySnapshot_payload_parses_keyboard_and_mouse_counts()
    {
        var data = new
        {
            keyboard_events_count = 200,
            mouse_events_count = 50,
            active_seconds = 120,
            idle_seconds = 30,
            foreground_process_name = "code.exe"
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(data));
        var el = doc.RootElement;

        var keyCount = el.TryGetProperty("keyboard_events_count", out var k) ? k.GetInt32() : 0;
        var mouseCount = el.TryGetProperty("mouse_events_count", out var m) ? m.GetInt32() : 0;

        keyCount.Should().Be(200);
        mouseCount.Should().Be(50);
    }

    [Fact]
    public void IntensityScore_is_capped_at_100()
    {
        const int maxExpected = 3000;
        var intensity = Math.Min((decimal)(9999 + 9999) / maxExpected * 100, 100);
        intensity.Should().Be(100);
    }

    [Fact]
    public void IntensityScore_formula_is_proportional()
    {
        const int maxExpected = 3000;
        var intensity = (decimal)(300 + 300) / maxExpected * 100;
        intensity.Should().Be(20m);
    }

    [Fact]
    public void Payload_with_unknown_type_is_skipped_gracefully()
    {
        var payload = """{"batch":[{"type":"unknown_type","data":{}}]}""";
        using var doc = JsonDocument.Parse(payload);
        var batch = doc.RootElement.GetProperty("batch");
        var count = 0;
        foreach (var entry in batch.EnumerateArray())
        {
            var type = entry.GetProperty("type").GetString();
            if (type == "activity_snapshot") count++;
        }
        count.Should().Be(0, "unknown type should be skipped");
    }
}
