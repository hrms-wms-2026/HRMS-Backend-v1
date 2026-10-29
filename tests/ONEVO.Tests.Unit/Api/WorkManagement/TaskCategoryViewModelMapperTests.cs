using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Tests.Unit.Api.WorkManagement;

/// <summary>The categories endpoints answer through this view model; it must carry the kind the Planner colours by.</summary>
public class TaskCategoryViewModelMapperTests
{
    [Fact]
    public void A_category_view_model_carries_its_kind()
    {
        var vm = new TaskCategoryResponse(Guid.NewGuid(), "Defects", 1, null, "bug").ToViewModel();

        Assert.Equal(("Defects", "bug", false), (vm.Name, vm.Kind, vm.IsSystem));
    }
}
