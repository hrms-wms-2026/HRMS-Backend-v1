using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Documents;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddMyDocument;

public sealed class AddMyDocumentCommandHandler : IRequestHandler<AddMyDocumentCommand, Result<EmployeeDocumentResponse>>
{
    public const int MaxNameLength = 150;

    private readonly Common.RepositoryInterfaces.IEmployeeRepository _employees;
    private readonly IEntityAssetRepository _assets;
    private readonly IFileStorageService _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public AddMyDocumentCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository employees,
        IEntityAssetRepository assets,
        IFileStorageService fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser)
    {
        _employees = employees;
        _assets = assets;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<EmployeeDocumentResponse>> Handle(AddMyDocumentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<EmployeeDocumentResponse>.Forbidden("Authentication required.");

        if (!EmployeeDocumentTypes.IsValid(request.DocumentType))
            return Result<EmployeeDocumentResponse>.Failure("Unknown document type.", 400);

        var name = request.Name?.Trim();
        if (name is { Length: > MaxNameLength })
            return Result<EmployeeDocumentResponse>.Failure($"Document name must be {MaxNameLength} characters or fewer.", 400);

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var employee = await _employees.GetByUserIdAsync(tenantId, userId, ct);
        if (employee is null)
            return Result<EmployeeDocumentResponse>.NotFound("No employee record for the current user.");

        var recordResult = await _fileStorage.GetRecordAsync(tenantId, request.FileId, ct);
        if (!recordResult.IsSuccess ||
            recordResult.Value!.DeletedAt is not null ||
            recordResult.Value.UploadedByUserId != userId)
        {
            return Result<EmployeeDocumentResponse>.NotFound("File not found.");
        }

        var record = recordResult.Value;
        var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.EmployeeDocument)!;
        var extension = Path.GetExtension(record.OriginalFileName);
        if (record.FileSizeBytes > rule.MaxSizeBytes ||
            !rule.AllowedContentTypes.Contains(record.ContentType, StringComparer.OrdinalIgnoreCase) ||
            !rule.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Result<EmployeeDocumentResponse>.Failure("The uploaded file is not a supported document.", 400);
        }

        if (await _assets.GetByFileRecordIdAsync(tenantId, request.FileId, ct) is not null)
            return Result<EmployeeDocumentResponse>.Conflict("The file is already linked.");

        var displayName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(record.OriginalFileName)
            : name;
        var metadataJson = new EmployeeDocumentMetadata(request.DocumentType, displayName).ToJson();
        var asset = new EntityAsset
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerType = EntityAssetOwnerTypes.EmployeeDocument,
            OwnerId = employee.Id,
            AssetPurpose = UploadPurposeCatalog.EmployeeDocument,
            FileRecordId = record.Id,
            IsPrimary = false,
            MetadataJson = metadataJson,
            CreatedByType = "user",
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _assets.AddAsync(asset, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<EmployeeDocumentResponse>.Success(EmployeeDocumentResponse.From(new EntityAssetWithFileAndMetadata(
            asset.Id, record.Id, record.OriginalFileName, record.FileSizeBytes, record.ContentType,
            asset.CreatedAt, asset.AssetPurpose, metadataJson)));
    }
}
