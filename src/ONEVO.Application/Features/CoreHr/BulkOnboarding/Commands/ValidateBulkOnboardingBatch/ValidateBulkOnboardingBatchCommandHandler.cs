using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;

public class ValidateBulkOnboardingBatchCommandHandler
    : IRequestHandler<ValidateBulkOnboardingBatchCommand, Result<ValidateBulkOnboardingBatchResult>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly IBulkOnboardingRowValidator _rowValidator;
    private readonly ICurrentUser _currentUser;

    public ValidateBulkOnboardingBatchCommandHandler(
        IBulkOnboardingBatchRepository batchRepository, IBulkOnboardingRowValidator rowValidator, ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _rowValidator = rowValidator;
        _currentUser = currentUser;
    }

    public async Task<Result<ValidateBulkOnboardingBatchResult>> Handle(
        ValidateBulkOnboardingBatchCommand request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetTrackedAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<ValidateBulkOnboardingBatchResult>.NotFound("The batch could not be found.");

        batch.ColumnMappingJson = JsonSerializer.Serialize(request.Mapping);

        var rows = await _batchRepository.ListTrackedRowsAsync(_currentUser.TenantId, batch.Id, ct);
        var emailsSeen = new HashSet<string>();
        var results = new List<RowValidationResultItem>();
        var validCount = 0;
        var invalidCount = 0;

        foreach (var row in rows.OrderBy(r => r.RowNumber))
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawDataJson) ?? new();
            var outcome = await _rowValidator.ValidateRowAsync(_currentUser.TenantId, batch, raw, request.Mapping, emailsSeen, ct);

            row.ResolvedDepartmentId = outcome.DepartmentId;
            row.ResolvedPositionId = outcome.PositionId;
            row.ResolvedReportsToEmployeeId = outcome.ReportsToEmployeeId;
            row.ResolvedTemplateId = outcome.TemplateId;
            row.Status = outcome.IsValid ? BulkOnboardingBatchRowStatus.Valid : BulkOnboardingBatchRowStatus.Invalid;
            row.ErrorMessage = outcome.ErrorMessage;

            if (outcome.IsValid) validCount++; else invalidCount++;
            results.Add(new RowValidationResultItem(row.RowNumber, row.Status, row.ErrorMessage));
        }

        batch.Status = BulkOnboardingBatchStatus.Validated;
        batch.ValidRows = validCount;
        batch.InvalidRows = invalidCount;

        await _batchRepository.SaveChangesAsync(ct);

        return Result<ValidateBulkOnboardingBatchResult>.Success(
            new ValidateBulkOnboardingBatchResult(validCount, invalidCount, results));
    }
}
