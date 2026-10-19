namespace ONEVO.Application.Common.Constants;

/// <summary>Centralized entity_assets.owner_type values. Add a new constant here — never a raw string literal — when a new owner type is wired up.</summary>
public static class EntityAssetOwnerTypes
{
    public const string Project = "project";
    public const string Objective = "objective";
    public const string Task = "task";
    public const string Comment = "comment";
    public const string Employee = "employee";
    public const string LegalEntity = "legal_entity";
    /// <summary>An employee's own uploaded documents (CV, ID, certificates). Kept separate from
    /// Employee so the coverage-free avatar read policy never applies to them.</summary>
    public const string EmployeeDocument = "employee_document";
    /// <summary>Attachments on a checklist template's tasks (owner = template id). Copied into the
    /// real Work task's own attachments when the checklist is provisioned for an employee.</summary>
    public const string ChecklistTemplate = "checklist_template";
}
