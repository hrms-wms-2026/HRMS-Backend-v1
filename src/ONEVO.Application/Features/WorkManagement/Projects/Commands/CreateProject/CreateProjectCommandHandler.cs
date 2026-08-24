using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;

public class CreateProjectCommandHandler : IRequestHandler<CreateProjectCommand, Result<ProjectCreationResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectCategoryRepository _categories;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IProjectVersionRepository _versions;
    private readonly IReleaseCalendarRepository _releaseCalendar;
    private readonly ILabelRepository _labels;
    private readonly ITaskStatusRepository _taskStatuses;
    private readonly ITaskCategoryRepository _taskCategories;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IEmployeeRepository _employees;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IAuditLogRepository _auditLogs;
    private readonly IFileStorageService _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateProjectCommandHandler>? _logger;

    public CreateProjectCommandHandler(
        ICurrentUser currentUser,
        IProjectCategoryRepository categories,
        IProjectRepository projects,
        IObjectiveRepository objectives,
        IProjectMemberRepository members,
        IProjectVersionRepository versions,
        IReleaseCalendarRepository releaseCalendar,
        ILabelRepository labels,
        ITaskStatusRepository taskStatuses,
        ITaskCategoryRepository taskCategories,
        IEntityAssetRepository entityAssets,
        IEmployeeRepository employees,
        ILegalEntityRepository legalEntities,
        IAuditLogRepository auditLogs,
        IFileStorageService fileStorage,
        IUnitOfWork unitOfWork,
        ILogger<CreateProjectCommandHandler>? logger = null)
    {
        _currentUser = currentUser;
        _categories = categories;
        _projects = projects;
        _objectives = objectives;
        _members = members;
        _versions = versions;
        _releaseCalendar = releaseCalendar;
        _labels = labels;
        _taskStatuses = taskStatuses;
        _taskCategories = taskCategories;
        _entityAssets = entityAssets;
        _employees = employees;
        _legalEntities = legalEntities;
        _auditLogs = auditLogs;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ProjectCreationResponse>> Handle(CreateProjectCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectCreationResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ProjectCreationResponse>.Forbidden("Tenant context missing.");

        var employee = await _employees.GetByUserIdAsync(tenantId, userId, ct);
        if (employee is null || employee.EmploymentStatusId != EmploymentStatusIds.Active)
            return Result<ProjectCreationResponse>.Forbidden("No employee record for the current user.");

        var employeeId = employee.Id;

        var legalEntity = await _legalEntities.GetPrimaryByTenantIdAsync(tenantId, ct);
        if (legalEntity is null)
            return Result<ProjectCreationResponse>.Forbidden("Tenant has no primary company configured.");

        var category = await _categories.GetByIdForTenantAsync(tenantId, request.CategoryId, ct);
        if (category is null || !category.IsActive)
            return Result<ProjectCreationResponse>.NotFound("Project category not found.");

        var identifier = request.Identifier.Trim().ToUpperInvariant();
        if (await _projects.IdentifierExistsForTenantAsync(tenantId, identifier, ct))
            return Result<ProjectCreationResponse>.Conflict("A project with this identifier already exists.");

        var normalizedLabelNames = request.Labels
            .Select(l => l.Name.Trim().ToLowerInvariant())
            .ToList();
        if (normalizedLabelNames.Distinct().Count() != normalizedLabelNames.Count)
            return Result<ProjectCreationResponse>.Conflict("Duplicate label names are not allowed in the same request.");

        FileRecordDto? uploadedLogo = null;
        if (request.LogoContent is not null && request.LogoFileName is not null && request.LogoContentType is not null)
        {
            var uploadResult = await _fileStorage.UploadAsync(
                tenantId, userId, request.LogoFileName, request.LogoContentType,
                UploadPurposeCatalog.ProjectCover, request.LogoContent, ct);

            if (!uploadResult.IsSuccess)
                return Result<ProjectCreationResponse>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

            uploadedLogo = uploadResult.Value;
        }

        FileRecordDto? uploadedBanner = null;
        if (request.BannerContent is not null && request.BannerFileName is not null && request.BannerContentType is not null)
        {
            var uploadResult = await _fileStorage.UploadAsync(
                tenantId, userId, request.BannerFileName, request.BannerContentType,
                UploadPurposeCatalog.ProjectBanner, request.BannerContent, ct);

            if (!uploadResult.IsSuccess)
                return Result<ProjectCreationResponse>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

            uploadedBanner = uploadResult.Value;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;

            var project = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OwningLegalEntityId = legalEntity.Id,
                CategoryId = category.Id,
                Name = request.Name.Trim(),
                Identifier = identifier,
                Description = request.Description?.Trim(),
                LeadId = employeeId,
                StartDate = request.StartDate,
                TargetDate = request.TargetDate,
                Color = request.Color,
                ActualHours = request.ActualHours,
                AllocatedHours = request.DefaultObjectiveAllocatedHours,
                CompletedHours = 0m,
                IsActive = true,
                CreatedById = userId,
                CreatedAt = now
            };

            var defaultObjective = new Objective
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                ParentObjectiveId = null,
                IsDefault = true,
                Title = project.Name,
                Description = project.Description,
                OwnerId = employeeId,
                IsActive = true,
                StartDate = project.StartDate,
                EndDate = project.TargetDate,
                Progress = 0m,
                ActualHours = project.ActualHours,
                AllocatedHours = request.DefaultObjectiveAllocatedHours,
                CompletedHours = 0m,
                CreatedById = userId,
                CreatedAt = now
            };

            var creatorMembership = new ProjectMember
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                ObjectiveId = defaultObjective.Id,
                EmployeeId = employeeId,
                MembershipSource = ProjectMembershipSources.System,
                IsActive = true,
                JoinedAt = now,
                CreatedById = userId,
                CreatedAt = now
            };

            var defaultVersion = new ProjectVersion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                Name = "Initial Release",
                StatusId = VersionStatusIds.Planned,
                CreatedById = userId,
                CreatedAt = now
            };

            var releaseReminder = new ReleaseCalendarEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                VersionId = defaultVersion.Id,
                RecipientUserId = userId,
                ScheduledDate = request.ReleaseDate ?? request.TargetDate,
                ReminderType = ReleaseReminderTypes.ProjectRelease,
                IsActive = true,
                CreatedById = userId,
                CreatedAt = now
            };

            var labels = request.Labels.Select(l => new Label
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                Name = l.Name.Trim(),
                Color = l.Color,
                CreatedById = userId,
                CreatedAt = now
            }).ToList();

            EntityAsset? logoAsset = null;
            if (uploadedLogo is not null)
            {
                logoAsset = new EntityAsset
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OwnerType = EntityAssetOwnerTypes.Project,
                    OwnerId = project.Id,
                    AssetPurpose = UploadPurposeCatalog.ProjectCover,
                    FileRecordId = uploadedLogo.Id,
                    IsPrimary = true,
                    CreatedByType = "user",
                    CreatedById = userId,
                    CreatedAt = now
                };
            }

            EntityAsset? bannerAsset = null;
            if (uploadedBanner is not null)
            {
                bannerAsset = new EntityAsset
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OwnerType = EntityAssetOwnerTypes.Project,
                    OwnerId = project.Id,
                    AssetPurpose = UploadPurposeCatalog.ProjectBanner,
                    FileRecordId = uploadedBanner.Id,
                    IsPrimary = true,
                    CreatedByType = "user",
                    CreatedById = userId,
                    CreatedAt = now
                };
            }

            await _projects.AddAsync(project, ct);
            await _objectives.AddAsync(defaultObjective, ct);
            await _taskStatuses.AddRangeAsync(
                DefaultTaskStatusTemplate.BuildRows(tenantId, project.Id, objectiveId: null, userId, now), ct);
            await _taskCategories.AddRangeAsync(
                DefaultTaskCategoryTemplate.BuildRows(tenantId, project.Id, userId, now), ct);
            await _members.AddAsync(creatorMembership, ct);
            await _versions.AddAsync(defaultVersion, ct);
            await _releaseCalendar.AddAsync(releaseReminder, ct);
            foreach (var label in labels)
                await _labels.AddAsync(label, ct);
            if (logoAsset is not null)
                await _entityAssets.AddAsync(logoAsset, ct);
            if (bannerAsset is not null)
                await _entityAssets.AddAsync(bannerAsset, ct);

            await _auditLogs.AddAsync(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Action = "project.created",
                ResourceType = "Project",
                ResourceId = project.Id,
                NewValuesJson = $"{{\"name\":\"{project.Name}\",\"identifier\":\"{project.Identifier}\"}}",
                CreatedAt = now
            }, ct);

            await _unitOfWork.SaveChangesAsync(ct);

            var response = new ProjectCreationResponse(
                ProjectMapper.ToSummary(project),
                ProjectMapper.ToSummary(defaultObjective),
                ProjectMapper.ToSummary(defaultVersion, "planned"),
                ProjectMapper.ToSummary(releaseReminder),
                labels.Select(ProjectMapper.ToSummary).ToList(),
                ProjectMapper.ToSummary(creatorMembership, userId),
                uploadedLogo is not null ? new ProjectLogoSummaryDto(uploadedLogo.Id, uploadedLogo.OriginalFileName) : null,
                uploadedBanner is not null ? new ProjectLogoSummaryDto(uploadedBanner.Id, uploadedBanner.OriginalFileName) : null);

            return Result<ProjectCreationResponse>.Success(response);
        }
        catch
        {
            if (uploadedLogo is not null)
            {
                // IFileStorageService has no method to reverse an already-Completed
                // upload (CancelReservationAsync explicitly rejects a completed
                // reservation with 409 - bytes have already moved to used). This
                // orphaned file_record is logged for manual/future reconciliation
                // rather than attempting a compensation call that cannot succeed -
                // the same documented limitation SetLegalEntityLogoCommandHandler
                // already accepts for file-ownership validation.
                _logger?.LogError(
                    "Project creation failed after logo upload completed. Orphaned file_record {FileRecordId} for tenant {TenantId} requires manual/future reconciliation.",
                    uploadedLogo.Id, tenantId);
            }
            if (uploadedBanner is not null)
            {
                _logger?.LogError(
                    "Project creation failed after banner upload completed. Orphaned file_record {FileRecordId} for tenant {TenantId} requires manual/future reconciliation.",
                    uploadedBanner.Id, tenantId);
            }
            throw;
        }
    }
}
