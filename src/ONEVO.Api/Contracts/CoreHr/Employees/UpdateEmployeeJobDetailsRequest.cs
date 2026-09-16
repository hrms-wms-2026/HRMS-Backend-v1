namespace ONEVO.Api.Contracts.CoreHr.Employees;

public sealed record UpdateEmployeeJobDetailsRequest(
    string EmployeeNumber, string EmploymentTypeCode, Guid? WorkModeId);
