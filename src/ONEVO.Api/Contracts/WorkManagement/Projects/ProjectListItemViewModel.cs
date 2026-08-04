namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public sealed record ProjectListItemViewModel(
    Guid Id, string Name, string Identifier, Guid CategoryId, Guid LeadId,
    DateOnly StartDate, DateOnly TargetDate, string? Color, bool IsActive,
    decimal AllocatedHours, decimal CompletedHours, bool IsLead);
