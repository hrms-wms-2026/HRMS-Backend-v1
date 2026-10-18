using Microsoft.Extensions.Configuration;
using ONEVO.Application.Features.WorkManagement.Meetings;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Meetings:Attendance is validated at startup (meetings-as-tasks Phase 2 spec 5.8): a bad checkpoint list or
/// window stops the app instead of silently mis-scheduling attendance checks.</summary>
public sealed class MeetingsOptionsValidatorTests
{
    private static readonly MeetingsOptionsValidator Validator = new();

    private static MeetingsOptions With(MeetingAttendanceOptions attendance) => new() { Attendance = attendance };

    [Fact]
    public void Defaults_AreValid()
    {
        var options = new MeetingsOptions();

        Assert.True(Validator.Validate(null, options).Succeeded);
        Assert.False(options.Create || options.BulkInvite || options.Background || options.ReadOnly);
        Assert.Equal([TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24)], options.Attendance.Checkpoints);
    }

    [Fact]
    public void BindingTheAppsettingsSection_ReplacesTheDefaultCheckpoints()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ONEVO.Api", "appsettings.json"))
            .Build();
        var options = new MeetingsOptions();

        MeetingsOptions.BindFrom(configuration.GetSection(MeetingsOptions.Section), options);

        Assert.Equal([TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24)], options.Attendance.Checkpoints);
        Assert.True(Validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("02:00:00", "00:30:00")]
    [InlineData("00:00:00")]
    [InlineData("00:30:00", "00:30:00")]
    public void Checkpoints_MustBeStrictlyIncreasingAndPositive(params string[] checkpoints)
    {
        var result = Validator.Validate(null, With(new MeetingAttendanceOptions { Checkpoints = checkpoints.Select(TimeSpan.Parse).ToArray() }));

        Assert.True(result.Failed);
        Assert.Contains("Meetings:Attendance:Checkpoints", result.FailureMessage);
    }

    [Fact]
    public void EmptyCheckpoints_Fail()
    {
        var result = Validator.Validate(null, With(new MeetingAttendanceOptions { Checkpoints = [] }));

        Assert.True(result.Failed);
        Assert.Contains("Meetings:Attendance:Checkpoints", result.FailureMessage);
    }

    [Theory]
    [InlineData(nameof(MeetingAttendanceOptions.MatchWindowBefore))]
    [InlineData(nameof(MeetingAttendanceOptions.LateRecheckWindow))]
    [InlineData(nameof(MeetingAttendanceOptions.FailedRetryAfter))]
    public void NonPositiveWindows_Fail(string window)
    {
        foreach (var value in new[] { TimeSpan.Zero, TimeSpan.FromMinutes(-5) })
        {
            var attendance = window switch
            {
                nameof(MeetingAttendanceOptions.MatchWindowBefore) => new MeetingAttendanceOptions { MatchWindowBefore = value },
                nameof(MeetingAttendanceOptions.LateRecheckWindow) => new MeetingAttendanceOptions { LateRecheckWindow = value },
                _ => new MeetingAttendanceOptions { FailedRetryAfter = value },
            };

            var result = Validator.Validate(null, With(attendance));

            Assert.True(result.Failed);
            Assert.Contains($"Meetings:Attendance:{window}", result.FailureMessage);
        }
    }
}
