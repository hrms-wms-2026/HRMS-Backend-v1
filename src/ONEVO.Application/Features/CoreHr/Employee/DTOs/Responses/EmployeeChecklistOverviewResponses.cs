namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Bypassed tasks are counted in Total but not in Completed.</summary>
public sealed record EmployeeChecklistGroup(string Name, string LifecycleType, int Completed, int Bypassed, int Total);

public sealed record EmployeeChecklistOverviewResponse(IReadOnlyList<EmployeeChecklistGroup> Groups);
