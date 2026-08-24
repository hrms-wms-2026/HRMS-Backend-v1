using System.Text.Json;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ResolveBulkOnboardingIssues;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public sealed class ResolveBulkOnboardingIssuesCommandHandlerTests
{
    private readonly Mock<IBulkOnboardingBatchRepository> _batches = new();
    private readonly Mock<IBulkOnboardingValidationRunner> _runner = new();
    private readonly Mock<IWorkModeRepository> _workModes = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<IDepartmentRepository> _departments = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _batchId = Guid.NewGuid();
    private readonly BulkOnboardingBatch _batch;

    public ResolveBulkOnboardingIssuesCommandHandlerTests()
    {
        _batch = new BulkOnboardingBatch
        {
            Id = _batchId,
            TenantId = _tenantId,
            LegalEntityId = Guid.NewGuid(),
            ColumnMappingJson = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["department"] = "Dept",
                ["firstName"] = "First Name",
            }),
            TotalRows = 2
        };
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _batches.Setup(b => b.GetTrackedAsync(_tenantId, _batchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_batch);
        _batches.Setup(b => b.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _runner.Setup(r => r.RunAsync(_batch, It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateBulkOnboardingBatchResult(2, 0, 2, [], []));
    }

    private ResolveBulkOnboardingIssuesCommandHandler CreateHandler() => new(
        _batches.Object, _runner.Object, _workModes.Object, _positions.Object, _departments.Object,
        _mediator.Object, _currentUser.Object);

    [Fact]
    public async Task MapExisting_StoresValueMapAndRevalidates()
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        var targetId = Guid.NewGuid().ToString();

        var result = await CreateHandler().Handle(new ResolveBulkOnboardingIssuesCommand(
            _batchId,
            "department_not_found:Human Resorces",
            BulkOnboardingIssueTypes.Actions.MapExisting,
            targetId,
            null, null, [2, 5], null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var state = BulkOnboardingResolutionStateSerializer.Deserialize(_batch.ResolutionStateJson);
        var map = Assert.Single(state.ValueMaps);
        Assert.Equal("department", map.Field);
        Assert.Equal("Human Resorces", map.ImportedValue);
        Assert.Equal(targetId, map.TargetId);
        Assert.Equal(BulkOnboardingIssueTypes.Actions.MapExisting, map.Action);
        _runner.Verify(r => r.RunAsync(_batch, It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateDepartment_WithoutOrgManage_Returns403()
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);

        var result = await CreateHandler().Handle(new ResolveBulkOnboardingIssuesCommand(
            _batchId,
            "department_not_found:Sales",
            BulkOnboardingIssueTypes.Actions.CreateDepartment,
            null, null, null, [1],
            new ResolveBulkOnboardingCreateDepartment("Sales", "SALES", null),
            null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _mediator.Verify(m => m.Send(It.IsAny<CreateDepartmentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateDepartment_WithOrgManage_CreatesAndMaps()
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);
        var createdId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<CreateDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentResponse>.Success(new DepartmentResponse(
                createdId, _batch.LegalEntityId, "Sales", "SALES", null, null, true, DateTimeOffset.UtcNow, null)));

        var result = await CreateHandler().Handle(new ResolveBulkOnboardingIssuesCommand(
            _batchId,
            "department_not_found:Sales",
            BulkOnboardingIssueTypes.Actions.CreateDepartment,
            null, null, null, [1],
            new ResolveBulkOnboardingCreateDepartment("Sales", "SALES", null),
            null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var state = BulkOnboardingResolutionStateSerializer.Deserialize(_batch.ResolutionStateJson);
        Assert.Equal(createdId.ToString(), state.ValueMaps[0].TargetId);
    }

    [Fact]
    public async Task SetDefault_WorkMode_UpdatesBatchDefault()
    {
        _workModes.Setup(w => w.ExistsActiveAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateHandler().Handle(new ResolveBulkOnboardingIssuesCommand(
            _batchId,
            BulkOnboardingIssueTypes.WorkModeMissing,
            BulkOnboardingIssueTypes.Actions.SetDefault,
            null, null, 2, [1, 3], null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _batch.DefaultWorkModeId);
    }

    [Fact]
    public async Task EditImportedValue_WritesRowOverrides()
    {
        var row = new BulkOnboardingBatchRow
        {
            BatchId = _batchId,
            RowNumber = 2,
            RawDataJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["Dept"] = "Human Resorces" })
        };
        _batches.Setup(b => b.ListTrackedRowsAsync(_tenantId, _batchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BulkOnboardingBatchRow> { row });

        var result = await CreateHandler().Handle(new ResolveBulkOnboardingIssuesCommand(
            _batchId,
            "department_not_found:Human Resorces",
            BulkOnboardingIssueTypes.Actions.EditImportedValue,
            null, "Human Resources", null, [2], null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var state = BulkOnboardingResolutionStateSerializer.Deserialize(_batch.ResolutionStateJson);
        var ov = Assert.Single(state.RowOverrides);
        Assert.Equal(2, ov.RowNumber);
        Assert.Equal("Human Resources", ov.Fields["department"]);
        Assert.Equal("Human Resorces", ov.OriginalFields["department"]);
    }

    [Fact]
    public async Task IncreaseCapacity_WithOrgManage_UpdatesPosition()
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);
        var positionId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        _positions.Setup(p => p.GetByIdAsync(_tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position
            {
                Id = positionId,
                TenantId = _tenantId,
                LegalEntityId = _batch.LegalEntityId,
                DepartmentId = departmentId,
                Name = "Project Manager",
                Code = "PM",
                MaxOccupancy = 1
            });
        _mediator.Setup(m => m.Send(It.IsAny<UpdatePositionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionResponse>.Success(
                new PositionResponse(
                    positionId, _batch.LegalEntityId, departmentId, "Project Manager", "PM",
                    "pooled", 4, null, true, DateTimeOffset.UtcNow, null,
                    null, null, 0, 0, true)));

        var result = await CreateHandler().Handle(new ResolveBulkOnboardingIssuesCommand(
            _batchId,
            "position_capacity_exceeded:Project Manager",
            BulkOnboardingIssueTypes.Actions.IncreaseCapacity,
            positionId.ToString(),
            "4",
            null,
            [1, 2, 3, 4],
            null,
            null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _mediator.Verify(m => m.Send(
            It.Is<UpdatePositionCommand>(c => c.PositionId == positionId && c.MaxOccupancy == 4),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IncreaseCapacity_WithoutOrgManage_Returns403()
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);

        var result = await CreateHandler().Handle(new ResolveBulkOnboardingIssuesCommand(
            _batchId,
            "position_capacity_exceeded:Project Manager",
            BulkOnboardingIssueTypes.Actions.IncreaseCapacity,
            Guid.NewGuid().ToString(),
            "5",
            null, null, null, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
