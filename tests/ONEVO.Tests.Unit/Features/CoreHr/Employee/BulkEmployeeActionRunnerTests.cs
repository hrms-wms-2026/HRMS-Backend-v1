using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Services;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class BulkEmployeeActionRunnerTests
{
    private readonly FakeUnitOfWork _uow = new();

    [Fact]
    public async Task MapsSuccessPendingAndFailure_AndCountsThem()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var outcomes = new Dictionary<Guid, BulkItemOutcome>
        {
            [a] = new(true, false, null),
            [b] = new(true, true, null),
            [c] = new(false, false, "You cannot change your own position.")
        };

        var response = await BulkEmployeeActionRunner.RunAsync(
            new[] { a, b, c }, (id, _) => Task.FromResult(outcomes[id]), _uow, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(new[] { "succeeded", "pendingApproval", "failed" }, response.Items.Select(i => i.Outcome));
        Assert.Equal("You cannot change your own position.", response.Items[2].Reason);
        Assert.Null(response.Items[0].Reason);
        Assert.Equal((1, 1, 1), (response.Succeeded, response.PendingApproval, response.Failed));
    }

    [Fact]
    public async Task DeduplicatesIds_PreservingFirstOccurrenceOrder()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var calls = new List<Guid>();

        var response = await BulkEmployeeActionRunner.RunAsync(
            new[] { a, b, a }, (id, _) => { calls.Add(id); return Task.FromResult(new BulkItemOutcome(true, false, null)); },
            _uow, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(new[] { a, b }, calls);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task ClearsTrackingAfterEveryItem_IncludingFailures()
    {
        await BulkEmployeeActionRunner.RunAsync(
            new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() },
            (_, _) => Task.FromResult(new BulkItemOutcome(false, false, "nope")),
            _uow, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(3, _uow.ClearTrackingCallCount);
    }

    [Fact]
    public async Task UnexpectedException_BecomesFailedItem_AndProcessingContinues()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();

        var response = await BulkEmployeeActionRunner.RunAsync(
            new[] { a, b },
            (id, _) => id == a ? throw new InvalidOperationException("boom") : Task.FromResult(new BulkItemOutcome(true, false, null)),
            _uow, NullLogger.Instance, CancellationToken.None);

        Assert.Equal("failed", response.Items[0].Outcome);
        Assert.Equal(BulkEmployeeActionRunner.UnexpectedFailureReason, response.Items[0].Reason);
        Assert.Equal("succeeded", response.Items[1].Outcome);
        Assert.Equal(2, _uow.ClearTrackingCallCount);
    }

    [Fact]
    public async Task InnerValidationException_BecomesFailedItem_WithTheValidationMessage()
    {
        var a = Guid.NewGuid();

        var response = await BulkEmployeeActionRunner.RunAsync(
            new[] { a },
            (_, _) => throw new FluentValidation.ValidationException(new[] { new FluentValidation.Results.ValidationFailure("EmploymentTypeCode", "'Employment Type Code' must not be empty.") }),
            _uow, NullLogger.Instance, CancellationToken.None);

        Assert.Equal("failed", response.Items[0].Outcome);
        Assert.Equal("'Employment Type Code' must not be empty.", response.Items[0].Reason);
        Assert.Equal(1, _uow.ClearTrackingCallCount);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BulkEmployeeActionRunner.RunAsync(
            new[] { Guid.NewGuid() }, (_, _) => Task.FromResult(new BulkItemOutcome(true, false, null)),
            _uow, NullLogger.Instance, cts.Token));
    }
}
