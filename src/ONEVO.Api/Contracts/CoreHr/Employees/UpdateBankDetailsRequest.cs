namespace ONEVO.Api.Contracts.CoreHr.Employees;

public record UpdateBankDetailsRequest(
    string BankName, string BranchName, string AccountHolderName,
    string AccountNumber, string AccountType, string? RoutingNumber);
