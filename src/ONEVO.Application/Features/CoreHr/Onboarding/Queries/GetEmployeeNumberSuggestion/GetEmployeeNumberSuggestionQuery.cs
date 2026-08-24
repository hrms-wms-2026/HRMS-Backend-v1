using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetEmployeeNumberSuggestion;

public sealed record EmployeeNumberSuggestionResponse(string EmployeeNumber, string Prefix, int Sequence);

public sealed record GetEmployeeNumberSuggestionQuery(Guid LegalEntityId)
    : IRequest<Result<EmployeeNumberSuggestionResponse>>;
