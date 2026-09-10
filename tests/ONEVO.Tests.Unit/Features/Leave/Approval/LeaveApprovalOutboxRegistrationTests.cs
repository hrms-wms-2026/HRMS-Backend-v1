using System.Text.Json;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.OutboxHandlers;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public sealed class LeaveApprovalOutboxRegistrationTests
{
    [Theory]
    [InlineData(OutboxMessageTypes.LeaveRequestApproved)]
    [InlineData(OutboxMessageTypes.LeaveRequestRejected)]
    [InlineData(OutboxMessageTypes.LeaveInformationRequested)]
    [InlineData(OutboxMessageTypes.LeaveRequestCancelled)]
    public async Task EmailHandler_CompletesWithoutThrowing_ForEmptyPayload(string type)
    {
        var handler = new LeaveApprovalEmailOutboxHandler(
            Mock.Of<IEmailService>(), Mock.Of<IEmployeeRepository>(), type);

        Assert.Equal(type, handler.Type);
        await handler.HandleAsync("{}", CancellationToken.None);
    }

    [Fact]
    public async Task ApprovedHandler_SendsEmailWhenEmployeeHasAddress()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var email = new Mock<IEmailService>();
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId, TenantId = tenantId, Email = "anu@acme.com", FirstName = "Anu", LastName = "Raman", HireDate = new DateOnly(2024, 1, 1) });
        var handler = new LeaveApprovalEmailOutboxHandler(email.Object, employees.Object, OutboxMessageTypes.LeaveRequestApproved);
        var payload = JsonSerializer.Serialize(new LeaveRequestApprovedPayload(
            tenantId, Guid.NewGuid(), employeeId, Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.Zero),
            24m, 0m, Guid.NewGuid()));

        await handler.HandleAsync(payload, CancellationToken.None);

        email.Verify(x => x.SendAsync("anu@acme.com", It.Is<string>(s => s.Contains("approved")), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
