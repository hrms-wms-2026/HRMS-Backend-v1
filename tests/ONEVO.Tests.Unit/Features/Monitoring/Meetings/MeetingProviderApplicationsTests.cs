using FluentAssertions;
using ONEVO.Application.Features.Monitoring.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Meetings;

public sealed class MeetingProviderApplicationsTests
{
    [Theory]
    [InlineData(CalendarEventMeetingProviders.MicrosoftTeams, "ms-teams.exe", true)]
    [InlineData(CalendarEventMeetingProviders.MicrosoftTeams, "Teams.exe", true)]
    [InlineData(CalendarEventMeetingProviders.MicrosoftTeams, "Zoom.exe", false)]
    [InlineData(CalendarEventMeetingProviders.Zoom, "Zoom.exe", true)]
    [InlineData(CalendarEventMeetingProviders.Zoom, "ms-teams.exe", false)]
    public void A_system_created_meeting_counts_only_its_own_providers_app(string provider, string process, bool counts)
        => MeetingProviderApplications.ProcessStemsFor(provider)!
            .Contains(MeetingProviderApplications.Stem(process)).Should().Be(counts);

    [Theory]
    [InlineData(null)]
    [InlineData("google_meet")]
    public void A_pasted_link_or_unknown_provider_falls_back_to_the_meeting_category(string? provider)
        => MeetingProviderApplications.ProcessStemsFor(provider).Should().BeNull();
}
