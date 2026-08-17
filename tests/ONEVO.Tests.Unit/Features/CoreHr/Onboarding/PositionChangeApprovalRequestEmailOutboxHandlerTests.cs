using System.Text.Json;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.OutboxHandlers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class PositionChangeApprovalRequestEmailOutboxHandlerTests
{
    [Fact]
    public async Task HandleAsync_DeserializesPayload_AndSendsApprovalRequestEmail()
    {
        var email = new Mock<IEmailService>();
        email
            .Setup(e => e.SendPositionChangeApprovalRequestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new PositionChangeApprovalRequestEmailOutboxHandler(email.Object);
        Assert.Equal(OutboxMessageTypes.PositionChangeApprovalRequestEmail, handler.Type);

        var payload = new PositionChangeApprovalRequestEmailPayload(
            TenantId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ApproverUserId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            AccessGrantRequestId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ApproverEmail: "approver@test.dev",
            EmployeeName: "Ada Lovelace",
            PositionName: "CFO",
            ChangeReason: "promotion");
        var json = JsonSerializer.Serialize(payload);

        await handler.HandleAsync(json, CancellationToken.None);

        email.Verify(e => e.SendPositionChangeApprovalRequestAsync(
            "approver@test.dev", "Ada Lovelace", "CFO", "promotion", It.IsAny<CancellationToken>()), Times.Once);
    }
}
