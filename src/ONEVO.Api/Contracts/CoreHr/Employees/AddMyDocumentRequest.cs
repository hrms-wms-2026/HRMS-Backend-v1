namespace ONEVO.Api.Contracts.CoreHr.Employees;

/// <summary>Links an uploaded employee_document file to the caller's documents. Type is one of
/// cv | identity | certificate | other; Name falls back to the file name when blank.</summary>
public sealed record AddMyDocumentRequest(Guid FileId, string Type, string? Name);
