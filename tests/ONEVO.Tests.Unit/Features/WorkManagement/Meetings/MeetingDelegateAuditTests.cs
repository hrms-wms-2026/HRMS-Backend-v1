using System.Text.Json.Nodes;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Spec "Audit log": one row per delegated action or grant change, the delegate as the user, the principal in the
/// JSON, private titles masked, and no guest emails, descriptions or notes.</summary>
public sealed class MeetingDelegateAuditTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid DelegateUser = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly List<AuditLog> _rows = [];

    private MeetingDelegateAudit Sut()
    {
        var logs = new Mock<IAuditLogRepository>();
        logs.Setup(l => l.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLog row, CancellationToken _) => _rows.Add(row)).Returns(Task.CompletedTask);
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.TenantId).Returns(Tenant);
        user.SetupGet(u => u.UserId).Returns(DelegateUser);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        return new MeetingDelegateAudit(logs.Object, user.Object, clock.Object);
    }

    private static MeetingSeries Series(bool isPrivate) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, OrganizerEmployeeId = Guid.NewGuid(), Title = "Board review", IsPrivate = isPrivate,
    };

    private static JsonObject Json(string? text) => JsonNode.Parse(text!)!.AsObject();

    [Fact]
    public async Task RecordDelegation_Granted_WritesOneMeetingDelegateRow()
    {
        var grant = new MeetingDelegate
        {
            Id = Guid.NewGuid(), TenantId = Tenant, PrincipalEmployeeId = Guid.NewGuid(), DelegateEmployeeId = Guid.NewGuid(),
            ExpiresAt = Now.AddDays(30),
        };

        await Sut().RecordDelegationAsync(grant, MeetingAuditActions.DelegateGranted, CancellationToken.None);

        var row = Assert.Single(_rows);
        Assert.Equal((Tenant, DelegateUser, "meeting_delegate.granted", "MeetingDelegate", grant.Id, Now),
            (row.TenantId, row.UserId, row.Action, row.ResourceType, row.ResourceId, row.CreatedAt));
        var json = Json(row.NewValuesJson);
        Assert.Equal(grant.PrincipalEmployeeId, json["principalEmployeeId"]!.GetValue<Guid>());
        Assert.Equal(grant.DelegateEmployeeId, json["delegateEmployeeId"]!.GetValue<Guid>());
        Assert.NotNull(json["expiresAt"]);
        Assert.Null(row.OldValuesJson);
    }

    [Fact]
    public async Task RecordDelegation_Revoked_KeepsTheOldExpiry()
    {
        var grant = new MeetingDelegate
        {
            Id = Guid.NewGuid(), TenantId = Tenant, PrincipalEmployeeId = Guid.NewGuid(), DelegateEmployeeId = Guid.NewGuid(),
            ExpiresAt = Now.AddDays(3), RevokedAt = Now,
        };

        await Sut().RecordDelegationAsync(grant, MeetingAuditActions.DelegateRevoked, CancellationToken.None);

        var row = Assert.Single(_rows);
        Assert.Equal("meeting_delegate.revoked", row.Action);
        Assert.Null(Json(row.NewValuesJson)["expiresAt"]);
        Assert.NotNull(Json(row.OldValuesJson)["expiresAt"]);
    }

    [Fact]
    public async Task RecordForPrincipal_WritesTheDelegateAsUser_AndThePrincipalInTheJson()
    {
        var series = Series(isPrivate: false);

        await Sut().RecordForPrincipalAsync(series, series.OrganizerEmployeeId, MeetingAuditActions.Moved,
            new { scope = "this", newStart = Now.AddDays(1) }, new { start = Now }, CancellationToken.None);

        var row = Assert.Single(_rows);
        Assert.Equal((DelegateUser, "meeting.moved_for_principal", "MeetingSeries", series.Id),
            (row.UserId, row.Action, row.ResourceType, row.ResourceId));
        Assert.Equal(series.OrganizerEmployeeId, Json(row.NewValuesJson)["actingForEmployeeId"]!.GetValue<Guid>());
        Assert.Equal("this", Json(row.NewValuesJson)["scope"]!.GetValue<string>());
        Assert.NotNull(Json(row.OldValuesJson)["start"]);
    }

    [Fact]
    public async Task RecordForPrincipal_MasksAPrivateTitle_InBothNewAndOldValues()
    {
        var series = Series(isPrivate: true);

        await Sut().RecordForPrincipalAsync(series, series.OrganizerEmployeeId, MeetingAuditActions.Edited,
            new { title = "Salary talk with Ann" }, new { title = "Salary talk" }, CancellationToken.None);

        var row = Assert.Single(_rows);
        Assert.Equal("(private)", Json(row.NewValuesJson)["title"]!.GetValue<string>());
        Assert.Equal("(private)", Json(row.OldValuesJson)["title"]!.GetValue<string>());
        Assert.DoesNotContain("Salary", row.NewValuesJson + row.OldValuesJson);
    }

    [Fact]
    public async Task RecordForPrincipal_NeverWritesGuestEmailsDescriptionOrNotes()
    {
        var series = Series(isPrivate: false);

        await Sut().RecordForPrincipalAsync(series, series.OrganizerEmployeeId, MeetingAuditActions.Edited,
            new { title = "Board review", description = "secret agenda", notes = "minutes", guestEmails = new[] { "x@client.com" } },
            new { description = "old agenda", notes = "old minutes" }, CancellationToken.None);

        var row = Assert.Single(_rows);
        var text = row.NewValuesJson + row.OldValuesJson;
        Assert.DoesNotContain("secret agenda", text);
        Assert.DoesNotContain("minutes", text);
        Assert.DoesNotContain("x@client.com", text);
        Assert.Equal("Board review", Json(row.NewValuesJson)["title"]!.GetValue<string>());
    }
}
