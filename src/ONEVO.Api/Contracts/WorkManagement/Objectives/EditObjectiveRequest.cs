namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class EditObjectiveRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal AllocatedHours { get; set; }
}
