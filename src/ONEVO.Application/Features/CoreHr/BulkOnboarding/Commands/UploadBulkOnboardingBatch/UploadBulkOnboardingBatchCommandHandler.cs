using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;

public class UploadBulkOnboardingBatchCommandHandler
    : IRequestHandler<UploadBulkOnboardingBatchCommand, Result<BulkOnboardingBatchResponse>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IUploadPurposePolicy _uploadPolicy;

    public UploadBulkOnboardingBatchCommandHandler(
        IBulkOnboardingBatchRepository batchRepository, ICurrentUser currentUser, IUploadPurposePolicy uploadPolicy)
    {
        _batchRepository = batchRepository;
        _currentUser = currentUser;
        _uploadPolicy = uploadPolicy;
    }

    public async Task<Result<BulkOnboardingBatchResponse>> Handle(
        UploadBulkOnboardingBatchCommand request, CancellationToken ct)
    {
        var extension = Path.GetExtension(request.OriginalFileName).ToLowerInvariant();
        if (extension is ".csv" or ".xlsx")
        {
            // Task 23: the file is parsed, never stored, but the bytes must still be what the name says
            // (.xlsx is a zip, .csv is text) before a parser sees them.
            using var probe = new MemoryStream(request.FileContent, writable: false);
            var signature = _uploadPolicy.ValidateSignature(request.OriginalFileName, probe);
            if (!signature.IsSuccess)
                return Result<BulkOnboardingBatchResponse>.Failure(signature.Error!, signature.StatusCode ?? 400);
        }

        Result<ParsedBatchFile> parsed = extension switch
        {
            ".csv" => CsvBatchParser.Parse(System.Text.Encoding.UTF8.GetString(request.FileContent)),
            ".xlsx" => XlsxBatchParser.Parse(request.FileContent),
            _ => Result<ParsedBatchFile>.Failure("Upload a .csv or .xlsx file."),
        };

        if (!parsed.IsSuccess)
            return Result<BulkOnboardingBatchResponse>.Failure(parsed.Error!);

        var batch = new BulkOnboardingBatch
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            LegalEntityId = request.LegalEntityId,
            DefaultWorkModeId = request.DefaultWorkModeId,
            DefaultEmploymentType = request.DefaultEmploymentType,
            DefaultChecklistTemplateId = request.DefaultChecklistTemplateId,
            OriginalFileName = request.OriginalFileName,
            Status = BulkOnboardingBatchStatus.MappingPending,
            TotalRows = parsed.Value!.Rows.Count,
            CreatedByUserId = _currentUser.UserId,
        };

        var rows = parsed.Value.Rows.Select((rowData, index) => new BulkOnboardingBatchRow
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            BatchId = batch.Id,
            RowNumber = index + 1,
            RawDataJson = JsonSerializer.Serialize(rowData),
            Status = BulkOnboardingBatchRowStatus.PendingMapping,
        }).ToList();

        await _batchRepository.AddAsync(batch, rows, ct);
        await _batchRepository.SaveChangesAsync(ct);

        var suggestedMapping = ColumnMappingSuggester.Suggest(parsed.Value.Headers);

        return Result<BulkOnboardingBatchResponse>.Success(new BulkOnboardingBatchResponse(
            batch.Id, batch.Status, batch.TotalRows, null, null, parsed.Value.Headers, suggestedMapping));
    }
}
