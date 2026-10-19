namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

/// <summary>One step of the chosen manager's line, top of the organisation first. A vacant
/// position in the line appears as Kind "vacant" with no employee.</summary>
public record ReportingChainNodeResponse(
    Guid? EmployeeId,
    string? FirstName,
    string? LastName,
    string? PositionName,
    string? DepartmentName,
    string Kind = "person",
    string? ReportingLine = null);

public record OnboardingClockInDefaultsResponse(bool PhotoRequired, bool LocationCapture);

/// <summary>Anyone with an active seat in the company - for the Reporting step's
/// "Choose someone else" search (a one-off manager from any reporting line).</summary>
public record ManagerCandidateResponse(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    Guid PositionId,
    string PositionName,
    string? DepartmentName);
