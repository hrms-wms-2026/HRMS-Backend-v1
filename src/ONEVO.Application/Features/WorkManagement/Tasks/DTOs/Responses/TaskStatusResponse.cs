namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

// RequiresApproval/ApproverId below: dead fields, round-tripped for backward compatibility only -
// see TaskStatus.RequiresApproval's doc comment (WM-01, docs/superpowers/plans/next/2026-10-12-pre-uat-audit-wm-sec-findings.md).
public sealed record TaskStatusResponse(
    Guid Id, string Name, int DisplayOrder, bool RequiresApproval,
    Guid? ApproverId, bool MarksTaskComplete, string Visibility,
    string Category, string Color);
