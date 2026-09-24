namespace ONEVO.Api.Contracts.CoreHr.Onboarding;

public sealed record EmployeeNumberSuggestionViewModel(string EmployeeNumber, string Prefix, int Sequence);

public sealed record EmployeeNumberAvailabilityViewModel(string EmployeeNumber, bool Available);
