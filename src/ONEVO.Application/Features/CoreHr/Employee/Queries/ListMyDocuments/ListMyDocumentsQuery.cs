using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.Documents;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.ListMyDocuments;

public sealed record ListMyDocumentsQuery : IRequest<Result<IReadOnlyList<EmployeeDocumentResponse>>>;
