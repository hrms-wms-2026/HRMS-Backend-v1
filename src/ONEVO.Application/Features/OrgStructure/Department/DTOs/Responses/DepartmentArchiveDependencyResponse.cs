namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentArchiveDependencyResponse(
    Guid DepartmentId,
    bool CanArchive,
    DepartmentArchiveBlockers Blockers,
    string Message);
