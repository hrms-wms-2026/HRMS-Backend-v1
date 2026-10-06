namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

/// <summary>One status row exactly as it existed right before a project.status_template_change was
/// applied - enough to re-create it (if the apply deleted it) or restore its fields (if the apply
/// modified it) on revert.</summary>
public sealed record ProjectStatusTemplateSnapshotEntry(
    Guid Id, string Name, int DisplayOrder, string Category, string Color, string Visibility, bool MarksTaskComplete);

public sealed record ProjectStatusTemplateUndoSnapshot(IReadOnlyList<ProjectStatusTemplateSnapshotEntry> Statuses);
