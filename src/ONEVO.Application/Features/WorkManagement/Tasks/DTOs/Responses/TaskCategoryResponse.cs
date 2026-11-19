namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public record TaskCategoryResponse(Guid Id, string Name, int DisplayOrder, string? SystemKey, string Kind, string? Icon = null)
{
    public bool IsSystem => SystemKey is not null;
}
