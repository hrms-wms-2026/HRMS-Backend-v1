using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using System.Text.Json;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public sealed class BulkOnboardingPositionCapacityValidationTests
{
    private readonly Mock<IBulkOnboardingBatchRepository> _batches = new();
    private readonly Mock<IBulkOnboardingRowValidator> _rowValidator = new();
    private readonly Mock<IDepartmentRepository> _departments = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Mock<IWorkModeRepository> _workModes = new();
    private readonly Mock<IChecklistTemplateRepository> _templates = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _batchId = Guid.NewGuid();
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly Guid _otherPositionId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();

    public BulkOnboardingPositionCapacityValidationTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);
        _departments.Setup(d => d.ListByLegalEntityAsync(_tenantId, _legalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department>
            {
                new() { Id = _departmentId, Name = "Engineering", LegalEntityId = _legalEntityId, TenantId = _tenantId }
            });
        _workModes.Setup(w => w.ListActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _templates.Setup(t => t.ListOnboardingMatchesAsync(_tenantId, _legalEntityId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private BulkOnboardingValidationRunner CreateRunner() => new(
        _batches.Object, _rowValidator.Object, _departments.Object, _positions.Object,
        _assignments.Object, _workModes.Object, _templates.Object, _currentUser.Object);

    private BulkOnboardingBatch CreateBatch(int totalRows = 4) => new()
    {
        Id = _batchId,
        TenantId = _tenantId,
        LegalEntityId = _legalEntityId,
        TotalRows = totalRows,
        ColumnMappingJson = "{}"
    };

    private Position CreatePosition(Guid id, string name, int maxOccupancy) => new()
    {
        Id = id,
        TenantId = _tenantId,
        LegalEntityId = _legalEntityId,
        DepartmentId = _departmentId,
        Name = name,
        MaxOccupancy = maxOccupancy,
        Code = "PM"
    };

    private static BulkOnboardingBatchRow CreateRow(Guid batchId, int rowNumber) => new()
    {
        BatchId = batchId,
        RowNumber = rowNumber,
        RawDataJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["Position"] = "Project Manager" }),
        Status = BulkOnboardingBatchRowStatus.PendingMapping
    };

    private static RowValidationOutcome ValidWithPosition(Guid positionId, Guid departmentId) => new(
        true, null, departmentId, positionId, null,
        "A", "B", $"{Guid.NewGuid():N}@x.com", DateOnly.FromDateTime(DateTime.UtcNow),
        "full_time", 1, null, null);

    [Fact]
    public async Task RunAsync_DetectsPositionCapacityExceeded_WhenBatchNeedsExceedSeats()
    {
        var batch = CreateBatch();
        var position = CreatePosition(_positionId, "Project Manager", maxOccupancy: 2);
        var rows = Enumerable.Range(1, 4).Select(n => CreateRow(_batchId, n)).ToList();

        _batches.Setup(b => b.ListTrackedRowsAsync(_tenantId, _batchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _positions.Setup(p => p.ListByLegalEntityAsync(_tenantId, _legalEntityId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { position });
        _positions.Setup(p => p.ListByLegalEntityAsync(
                _tenantId, _legalEntityId, false, _departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { position });
        _assignments.Setup(a => a.GetOccupancyPreviewsAsync(
                _tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionOccupancyPreview>
            {
                [_positionId] = new PositionOccupancyPreview(1, [])
            });

        _rowValidator.Setup(v => v.ValidateRowAsync(
                _tenantId, batch, It.IsAny<Dictionary<string, string>>(),
                It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<ISet<string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<BulkOnboardingResolutionState?>()))
            .ReturnsAsync(ValidWithPosition(_positionId, _departmentId));

        var result = await CreateRunner().RunAsync(batch, new Dictionary<string, string?>(), CancellationToken.None);

        Assert.Equal(0, result.ValidRows);
        Assert.Equal(4, result.InvalidRows);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(BulkOnboardingIssueTypes.PositionCapacityExceeded, issue.IssueType);
        Assert.NotNull(issue.Context);
        Assert.Equal(1, issue.Context!.AvailableSeats);
        Assert.Equal(4, issue.Context.RequiredSeatsInBatch);
        Assert.Equal(1, issue.Context.CurrentPrimaryAssignments);
        Assert.Equal("Project Manager", issue.Context.PositionName);
        Assert.Equal(4, issue.AffectedRowCount);
    }

    [Fact]
    public async Task RunAsync_CountsOnlyRowsTargetingSamePosition()
    {
        var batch = CreateBatch(3);
        var pm = CreatePosition(_positionId, "Project Manager", maxOccupancy: 1);
        var eng = CreatePosition(_otherPositionId, "Engineer", maxOccupancy: 5);
        var rows = Enumerable.Range(1, 3).Select(n => CreateRow(_batchId, n)).ToList();

        _batches.Setup(b => b.ListTrackedRowsAsync(_tenantId, _batchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _positions.Setup(p => p.ListByLegalEntityAsync(_tenantId, _legalEntityId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { pm, eng });
        _positions.Setup(p => p.ListByLegalEntityAsync(
                _tenantId, _legalEntityId, false, _departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { pm, eng });
        _assignments.Setup(a => a.GetOccupancyPreviewsAsync(
                _tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionOccupancyPreview>());

        var call = 0;
        _rowValidator.Setup(v => v.ValidateRowAsync(
                _tenantId, batch, It.IsAny<Dictionary<string, string>>(),
                It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<ISet<string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<BulkOnboardingResolutionState?>()))
            .ReturnsAsync(() =>
            {
                call++;
                return call <= 2
                    ? ValidWithPosition(_positionId, _departmentId)
                    : ValidWithPosition(_otherPositionId, _departmentId);
            });

        var result = await CreateRunner().RunAsync(batch, new Dictionary<string, string?>(), CancellationToken.None);

        var capacity = Assert.Single(result.Issues, i => i.IssueType == BulkOnboardingIssueTypes.PositionCapacityExceeded);
        Assert.Equal(2, capacity.AffectedRowCount);
        Assert.Equal(1, capacity.Context!.AvailableSeats);
        Assert.Equal(2, capacity.Context.RequiredSeatsInBatch);
        Assert.Equal(1, result.ValidRows);
        Assert.Equal(2, result.InvalidRows);
    }

    [Fact]
    public async Task RunAsync_UsesOccupancyPreviewPrimaryCounts()
    {
        var batch = CreateBatch(1);
        var position = CreatePosition(_positionId, "Project Manager", maxOccupancy: 1);
        var rows = new List<BulkOnboardingBatchRow> { CreateRow(_batchId, 1) };

        _batches.Setup(b => b.ListTrackedRowsAsync(_tenantId, _batchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _positions.Setup(p => p.ListByLegalEntityAsync(_tenantId, _legalEntityId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { position });
        _positions.Setup(p => p.ListByLegalEntityAsync(
                _tenantId, _legalEntityId, false, _departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { position });
        _assignments.Setup(a => a.GetOccupancyPreviewsAsync(
                _tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionOccupancyPreview>
            {
                [_positionId] = new PositionOccupancyPreview(1, [])
            });
        _rowValidator.Setup(v => v.ValidateRowAsync(
                _tenantId, batch, It.IsAny<Dictionary<string, string>>(),
                It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<ISet<string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<BulkOnboardingResolutionState?>()))
            .ReturnsAsync(ValidWithPosition(_positionId, _departmentId));

        var result = await CreateRunner().RunAsync(batch, new Dictionary<string, string?>(), CancellationToken.None);

        Assert.Equal(0, result.ValidRows);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(1, issue.Context!.CurrentPrimaryAssignments);
        Assert.Equal(0, issue.Context.AvailableSeats);
        // CountActiveAsync must not be used when preview already supplied assigned count.
        _assignments.Verify(
            a => a.CountActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
