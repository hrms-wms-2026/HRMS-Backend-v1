using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.Documents;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployeeDocuments;

/// <summary>An employee's personal documents as seen by HR / a manager (read-only).</summary>
public sealed record ListEmployeeDocumentsQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<EmployeeDocumentResponse>>>;
