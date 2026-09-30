using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.Services;
using ONEVO.Application.Features.CoreHr.Employee.Services;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Common;

public class BulkItemRunnerTests
{
    private readonly FakeUnitOfWork _uow = new();

    [Fact]
    public async Task MapsOutcomes_DeduplicatesIds_ClearsTrackingPerItem()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var results = await BulkItemRunner.RunAsync(new[] { a, b, a },
            (id, _) => Task.FromResult(id == a
                ? new BulkItemOutcome(true, false, null)
                : new BulkItemOutcome(false, false, "nope")),
            _uow, NullLogger.Instance, "Unexpected.", CancellationToken.None);

        Assert.Equal(new[] { (a, "succeeded", (string?)null), (b, "failed", "nope") },
            results.Select(r => (r.Id, r.Outcome, r.Reason)));
        Assert.Equal(2, _uow.ClearTrackingCallCount);
    }

    [Fact]
    public async Task ExceptionsBecomeFailures_WithCallerSuppliedReason_AndValidationMessages()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var results = await BulkItemRunner.RunAsync(new[] { a, b },
            (id, _) => id == a
                ? throw new InvalidOperationException("boom")
                : throw new FluentValidation.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure("X", "X is bad.")
                }),
            _uow, NullLogger.Instance, "Unexpected error while processing this task.", CancellationToken.None);

        Assert.Equal("Unexpected error while processing this task.", results[0].Reason);
        Assert.Equal("X is bad.", results[1].Reason);
    }
}
