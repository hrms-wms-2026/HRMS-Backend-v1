namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record TaskStatusResponse(Guid Id, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, bool MarksTaskComplete);
