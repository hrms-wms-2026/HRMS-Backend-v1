using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.Documents;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddMyDocument;

/// <summary>Links a pending employee_document upload to the caller's own document list.</summary>
public sealed record AddMyDocumentCommand(Guid FileId, string DocumentType, string? Name)
    : IRequest<Result<EmployeeDocumentResponse>>;
