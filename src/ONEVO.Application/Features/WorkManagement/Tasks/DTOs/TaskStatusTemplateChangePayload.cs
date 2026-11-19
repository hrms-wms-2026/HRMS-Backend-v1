namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

/// <summary>wm_approval_requests.payload_json for project.status_template_change.</summary>
public sealed record TaskStatusTemplateChangePayload(TaskStatusChangeSet Changes, string? Note);
