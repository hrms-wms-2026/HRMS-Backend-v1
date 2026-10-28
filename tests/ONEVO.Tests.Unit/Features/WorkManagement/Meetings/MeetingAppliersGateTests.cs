using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Appliers;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Final-review I4: Meetings:ReadOnly (the recovery switch) and Meetings:Create also stop approved meeting
/// requests from being applied; the request stays pending (Invalid) so it can be approved again later.</summary>
public sealed class MeetingAppliersGateTests
{
    private readonly Mock<IMeetingSeriesFactory> _factory = new(MockBehavior.Strict);
    private readonly Mock<IMeetingRepository> _meetings = new(MockBehavior.Strict);

    // Complete payloads: without the gate each applier would go on to read the (strict) repository and fail differently.
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static string CreatePayload()
    {
        var form = new ONEVO.Application.Features.WorkManagement.Meetings.Contracts.CreateMeetingRequest(Guid.NewGuid(), Guid.NewGuid(),
            "Design review", null, null, false, DateTimeOffset.UtcNow.AddDays(2), 45, false, "UTC", null, [], [], null);
        return System.Text.Json.JsonSerializer.Serialize(new ONEVO.Application.Features.WorkManagement.Meetings.Contracts.MeetingCreatePayload(form,
            new ONEVO.Application.Features.WorkManagement.Meetings.Contracts.MeetingApprovalSnapshot(form.Title, null, form.Start, 45, "UTC",
                "Does not repeat", "Requester", "Project / Root", [], [], null)), Json);
    }

    private static string MovePayload()
        => System.Text.Json.JsonSerializer.Serialize(new MeetingMovePayload(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(2),
            ONEVO.Application.Features.WorkManagement.Meetings.Contracts.MeetingScopes.All, Guid.NewGuid(), Guid.NewGuid()), Json);

    private static ApprovalApplyContext Context(string action)
        => new(new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ActionType = action },
            action == WorkActionTypes.MeetingCreate ? CreatePayload() : MovePayload(), Guid.NewGuid());

    public static TheoryData<bool, bool> BlockedFlags => new() { { true, true }, { false, false } };

    [Theory]
    [MemberData(nameof(BlockedFlags))]
    public async Task CreateApplier_AppliesNothing_WhileReadOnlyOrDisabled(bool create, bool readOnly)
    {
        var applier = new MeetingCreateApplier(Options.Create(new MeetingsOptions { Create = create, ReadOnly = readOnly }),
            _factory.Object, _meetings.Object, null!, null!, null!, null!);

        var outcome = await applier.ApplyAsync(Context(WorkActionTypes.MeetingCreate), CancellationToken.None);

        Assert.Equal(ApplyOutcomeKind.Invalid, outcome.Kind);
    }

    [Theory]
    [MemberData(nameof(BlockedFlags))]
    public async Task MoveApplier_AppliesNothing_WhileReadOnlyOrDisabled(bool create, bool readOnly)
    {
        var applier = new MeetingMoveApplier(Options.Create(new MeetingsOptions { Create = create, ReadOnly = readOnly }),
            null!, _meetings.Object, null!, null!);

        var outcome = await applier.ApplyAsync(Context(WorkActionTypes.MeetingMove), CancellationToken.None);

        Assert.Equal(ApplyOutcomeKind.Invalid, outcome.Kind);
    }
}
