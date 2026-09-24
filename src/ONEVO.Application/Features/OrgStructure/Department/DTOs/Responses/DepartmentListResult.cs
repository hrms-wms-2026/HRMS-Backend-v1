namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public sealed record DepartmentListResult(
    DepartmentListPageResponse? Flat,
    DepartmentTreeResponse? Tree);
