using ONEVO.Api.Contracts.WorkManagement.Objectives;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ObjectiveViewModelMapperTests
{
    [Fact]
    public void ToViewModel_ObjectiveDetailResponse_ForwardsOwnerAndReportingManagerNamesAndIsOwner()
    {
        var response = new ObjectiveDetailResponse(
            Id: Guid.NewGuid(), ProjectId: Guid.NewGuid(), ParentObjectiveId: null, IsDefault: false,
            Title: "Design Phase", Description: null,
            OwnerId: Guid.NewGuid(), ReportingManagerId: Guid.NewGuid(), CreatedById: Guid.NewGuid(),
            StartDate: new DateOnly(2026, 1, 1), EndDate: new DateOnly(2026, 2, 1),
            Progress: 50m, ActualHours: null, AllocatedHours: 20m, CompletedHours: 10m,
            IsActive: true, IsAchieved: false, AchievedAt: null,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null,
            OwnerName: "Arun Kumar", ReportingManagerName: "Diya Perera", IsOwner: true);

        var viewModel = response.ToViewModel();

        Assert.Equal("Arun Kumar", viewModel.OwnerName);
        Assert.Equal("Diya Perera", viewModel.ReportingManagerName);
        Assert.True(viewModel.IsOwner);
    }

    [Fact]
    public void ToViewModel_ObjectiveSubtreeNodeResponse_ForwardsOwnerNamesIsOwnerAndAchievedState()
    {
        var childResponse = new ObjectiveSubtreeNodeResponse(
            Id: Guid.NewGuid(), ProjectId: Guid.NewGuid(), ParentObjectiveId: Guid.NewGuid(), IsDefault: false,
            Title: "Child", Description: null,
            OwnerId: Guid.NewGuid(), ReportingManagerId: null, CreatedById: Guid.NewGuid(),
            StartDate: new DateOnly(2026, 1, 10), EndDate: new DateOnly(2026, 1, 20),
            Progress: 100m, ActualHours: null, AllocatedHours: 5m, CompletedHours: 5m,
            IsActive: true, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null,
            OwnerName: "Thivaharan", ReportingManagerName: null, IsOwner: false,
            IsAchieved: true, AchievedAt: DateTimeOffset.UtcNow,
            Children: []);

        var childViewModel = childResponse.ToViewModel();

        Assert.Equal("Thivaharan", childViewModel.OwnerName);
        Assert.False(childViewModel.IsOwner);
        Assert.True(childViewModel.IsAchieved);
        Assert.NotNull(childViewModel.AchievedAt);
    }
}
