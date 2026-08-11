namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class CreateObjectiveRequest
{
    public Guid ParentObjectiveId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal AllocatedHours { get; set; }
    public Guid? HeadUserId { get; set; }
}
