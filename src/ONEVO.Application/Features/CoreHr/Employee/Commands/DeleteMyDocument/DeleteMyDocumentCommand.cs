using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteMyDocument;

public sealed record DeleteMyDocumentCommand(Guid DocumentId) : IRequest<Result>;
