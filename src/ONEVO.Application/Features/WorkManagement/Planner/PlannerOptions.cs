namespace ONEVO.Application.Features.WorkManagement.Planner;

/// <summary>Day planner (2026-10-26 design, Part B). Off by default: every planner endpoint answers 503 planner_disabled.</summary>
public sealed class PlannerOptions
{
    public const string SectionName = "Planner";

    public bool Enabled { get; set; }
}
