using Moq;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddMyDocument;
using ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteMyDocument;
using ONEVO.Application.Features.CoreHr.Employee.Documents;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployeeDocuments;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Queries.ListMyDocuments;
using ONEVO.Application.Features.Storage.EntityAssets.Services;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;
using VisibleEmployeeRepo = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class MyDocumentsHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<CommonEmployeeRepo> _employees = new();
    private readonly Mock<IEntityAssetRepository> _assets = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public MyDocumentsHandlerTests()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(_userId);
        _employees.Setup(r => r.GetByUserIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });
    }

    private FileRecordDto Record(Guid id, string fileName = "resume.pdf", string contentType = "application/pdf",
        long size = 1024, Guid? uploadedBy = null) =>
        new(id, _tenantId, "key", fileName, fileName, contentType, size, "sha", "clean",
            DateTimeOffset.UtcNow, uploadedBy ?? _userId, null);

    private AddMyDocumentCommandHandler AddHandler() =>
        new(_employees.Object, _assets.Object, _fileStorage.Object, _unitOfWork.Object, _currentUser.Object);

    [Fact]
    public async Task Add_LinksFileAsEmployeeDocument_WithTypeAndNameMetadata()
    {
        var fileId = Guid.NewGuid();
        _fileStorage.Setup(f => f.GetRecordAsync(_tenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Record(fileId)));
        EntityAsset? added = null;
        _assets.Setup(a => a.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()))
            .Callback<EntityAsset, CancellationToken>((a, _) => added = a);

        var result = await AddHandler().Handle(new AddMyDocumentCommand(fileId, "cv", "  My Resume "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(EntityAssetOwnerTypes.EmployeeDocument, added!.OwnerType);
        Assert.Equal(_employeeId, added.OwnerId);
        Assert.Equal(UploadPurposeCatalog.EmployeeDocument, added.AssetPurpose);
        Assert.False(added.IsPrimary);
        Assert.Equal("My Resume", result.Value!.Name);
        Assert.Equal("cv", result.Value.DocumentType);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Add_FallsBackToFileName_WhenNameBlank()
    {
        var fileId = Guid.NewGuid();
        _fileStorage.Setup(f => f.GetRecordAsync(_tenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Record(fileId, "Bachelor Certificate.pdf")));

        var result = await AddHandler().Handle(new AddMyDocumentCommand(fileId, "certificate", " "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bachelor Certificate", result.Value!.Name);
    }

    [Fact]
    public async Task Add_RejectsUnknownType()
    {
        var result = await AddHandler().Handle(new AddMyDocumentCommand(Guid.NewGuid(), "passport", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Add_RejectsFileUploadedBySomeoneElse()
    {
        var fileId = Guid.NewGuid();
        _fileStorage.Setup(f => f.GetRecordAsync(_tenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Record(fileId, uploadedBy: Guid.NewGuid())));

        var result = await AddHandler().Handle(new AddMyDocumentCommand(fileId, "cv", null), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
        _assets.Verify(a => a.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Add_RejectsFileOverTenMegabytes()
    {
        var fileId = Guid.NewGuid();
        _fileStorage.Setup(f => f.GetRecordAsync(_tenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Record(fileId, size: 11 * 1024 * 1024)));

        var result = await AddHandler().Handle(new AddMyDocumentCommand(fileId, "cv", null), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyEmployeeDocumentPurpose_WithParsedMetadata()
    {
        var assets = new List<EntityAssetWithFileAndMetadata>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "id.png", 10, "image/png", DateTimeOffset.UtcNow,
                UploadPurposeCatalog.EmployeeDocument, "{\"documentType\":\"identity\",\"name\":\"National ID\"}"),
            new(Guid.NewGuid(), Guid.NewGuid(), "x.png", 10, "image/png", DateTimeOffset.UtcNow, "something_else", null)
        };
        _assets.Setup(a => a.ListWithMetadataByOwnerAsync(_tenantId, EntityAssetOwnerTypes.EmployeeDocument, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assets);

        var handler = new ListMyDocumentsQueryHandler(_employees.Object, _assets.Object, _currentUser.Object);
        var result = await handler.Handle(new ListMyDocumentsQuery(), CancellationToken.None);

        var doc = Assert.Single(result.Value!);
        Assert.Equal("National ID", doc.Name);
        Assert.Equal("identity", doc.DocumentType);
    }

    [Fact]
    public async Task Delete_RefusesAnotherEmployeesDocument()
    {
        var assetId = Guid.NewGuid();
        _assets.Setup(a => a.GetByIdForTenantAsync(_tenantId, assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityAsset { Id = assetId, OwnerType = EntityAssetOwnerTypes.EmployeeDocument, OwnerId = Guid.NewGuid() });

        var handler = new DeleteMyDocumentCommandHandler(_employees.Object, _assets.Object, _fileStorage.Object, _unitOfWork.Object, _currentUser.Object);
        var result = await handler.Handle(new DeleteMyDocumentCommand(assetId), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
        _assets.Verify(a => a.DeleteAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_RemovesLinkAndStoredFile()
    {
        var assetId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var asset = new EntityAsset { Id = assetId, OwnerType = EntityAssetOwnerTypes.EmployeeDocument, OwnerId = _employeeId, FileRecordId = fileId };
        _assets.Setup(a => a.GetByIdForTenantAsync(_tenantId, assetId, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var handler = new DeleteMyDocumentCommandHandler(_employees.Object, _assets.Object, _fileStorage.Object, _unitOfWork.Object, _currentUser.Object);
        var result = await handler.Handle(new DeleteMyDocumentCommand(assetId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _assets.Verify(a => a.DeleteAsync(asset, It.IsAny<CancellationToken>()), Times.Once);
        _fileStorage.Verify(f => f.DeleteAsync(_tenantId, _userId, fileId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private readonly Mock<VisibleEmployeeRepo> _visibleEmployees = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();

    private EmployeeDocumentAccess Access() =>
        new(_employees.Object, _visibleEmployees.Object, _scopeResolver.Object, _currentUser.Object);

    private void GrantPermissions(params string[] permissions) =>
        _currentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns<string>(permissions.Contains);

    private void MakeVisible(Guid employeeId) =>
        _visibleEmployees.Setup(r => r.GetVisibleByIdAsync(_tenantId, It.IsAny<EmployeeVisibilityScope>(), employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeListItemResponse(employeeId, "E-1", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null));

    [Fact]
    public async Task Access_OwnerCanAlwaysView()
    {
        GrantPermissions();
        Assert.True(await Access().CanViewAsync(_tenantId, _employeeId));
    }

    [Fact]
    public async Task Access_AllowsOthersWithinCoverage_WithoutAnyPermission()
    {
        // Relationship-based: managing the employee is the grant, no permission code needed.
        var other = Guid.NewGuid();
        GrantPermissions();
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeVisibilityScope.Unrestricted());
        MakeVisible(other);

        Assert.True(await Access().CanViewAsync(_tenantId, other));
    }

    [Fact]
    public async Task Access_AllowsOrgManageWithinCoverage()
    {
        var other = Guid.NewGuid();
        GrantPermissions("org:manage");
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeVisibilityScope.Unrestricted());
        MakeVisible(other);

        Assert.True(await Access().CanViewAsync(_tenantId, other));
    }

    [Fact]
    public async Task Access_DeniesOutsideCoverage()
    {
        GrantPermissions();
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeVisibilityScope.Unrestricted());

        Assert.False(await Access().CanViewAsync(_tenantId, Guid.NewGuid()));
    }

    [Fact]
    public async Task AccessPolicy_DelegatesToEmployeeDocumentAccess()
    {
        var access = new Mock<IEmployeeDocumentAccess>();
        access.Setup(a => a.CanViewAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var policy = new EmployeeDocumentAccessPolicy(access.Object);

        Assert.True(await policy.CanReadAsync(_tenantId, _employeeId));
        Assert.False(await policy.CanReadAsync(_tenantId, Guid.NewGuid()));
    }

    [Fact]
    public async Task ListEmployeeDocuments_ForbiddenWhenAccessDenied()
    {
        var other = Guid.NewGuid();
        _employees.Setup(r => r.GetByIdAsync(_tenantId, other, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = other, TenantId = _tenantId });
        var access = new Mock<IEmployeeDocumentAccess>();

        var handler = new ListEmployeeDocumentsQueryHandler(_employees.Object, access.Object, _assets.Object, _currentUser.Object);
        var result = await handler.Handle(new ListEmployeeDocumentsQuery(other), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        _assets.Verify(a => a.ListWithMetadataByOwnerAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListEmployeeDocuments_ReturnsDocumentsWhenAllowed()
    {
        var other = Guid.NewGuid();
        _employees.Setup(r => r.GetByIdAsync(_tenantId, other, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = other, TenantId = _tenantId });
        var access = new Mock<IEmployeeDocumentAccess>();
        access.Setup(a => a.CanViewAsync(_tenantId, other, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _assets.Setup(a => a.ListWithMetadataByOwnerAsync(_tenantId, EntityAssetOwnerTypes.EmployeeDocument, other, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFileAndMetadata>
            {
                new(Guid.NewGuid(), Guid.NewGuid(), "cv.pdf", 10, "application/pdf", DateTimeOffset.UtcNow,
                    UploadPurposeCatalog.EmployeeDocument, "{\"documentType\":\"cv\",\"name\":\"CV\"}")
            });

        var handler = new ListEmployeeDocumentsQueryHandler(_employees.Object, access.Object, _assets.Object, _currentUser.Object);
        var result = await handler.Handle(new ListEmployeeDocumentsQuery(other), CancellationToken.None);

        Assert.Equal("CV", Assert.Single(result.Value!).Name);
    }
}
