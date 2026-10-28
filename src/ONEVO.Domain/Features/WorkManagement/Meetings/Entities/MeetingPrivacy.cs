namespace ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

/// <summary>
/// The one title a private meeting shows wherever it may be read by someone who must not see it (spec D17): the
/// reporting-line attendance view and the monitoring/compliance view. The real title, the organizer, the agenda,
/// the participant list and the link are hidden - only this word remains.
/// <para>
/// It lives here, in the domain, because three features mask with it (Calendar's incident engine, the Meetings
/// attendance view and the Meetings compliance read) and a user-visible string must have exactly one home.
/// </para>
/// </summary>
public static class MeetingPrivacy
{
    /// <summary>What a private meeting is called when it may be listed but not named.</summary>
    public const string MaskedTitle = "Private meeting";
}
