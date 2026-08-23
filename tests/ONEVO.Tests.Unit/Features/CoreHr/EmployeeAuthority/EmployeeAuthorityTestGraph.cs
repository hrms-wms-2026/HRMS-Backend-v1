using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using AuthPermission = ONEVO.Domain.Features.Auth.Entities.Permission;
using DomainEmployee = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using DomainPosition = ONEVO.Domain.Features.OrgStructure.Entities.Position;
using DomainPositionAssignment = ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment;
using PositionAssignmentKind = ONEVO.Domain.Features.CoreHr.Entities.PositionAssignmentKind;
using PositionAssignmentStatus = ONEVO.Domain.Features.CoreHr.Entities.PositionAssignmentStatus;
using ManagementCoverageRecord = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Tests.Unit.Features.CoreHr.EmployeeAuthority;

/// <summary>
/// Hand-rolled in-memory graph backing fakes of every repository interface
/// EmployeeAuthorityResolver depends on. Deliberately no EF Core / DbContext anywhere: the
/// resolver's implementation is pure Application-layer logic over repository abstractions, so it
/// is tested the same way - unit tests here assert real business rules, not EF query
/// translation (which EfEmployeeRepository's own doc comment already flags as unreliable to lean
/// on for a unit-test double).
/// </summary>
internal sealed class EmployeeAuthorityTestGraph
{
    public Guid TenantId { get; } = Guid.NewGuid();
    public DateTimeOffset Now { get; } = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
    public IDateTimeProvider Clock => new FakeDateTimeProvider(Now);

    private readonly List<DomainEmployee> _employees = new();
    private readonly List<DomainPosition> _positions = new();
    private readonly List<DomainPositionAssignment> _assignments = new();
    private readonly List<ManagementCoverageRecord> _coverage = new();
    private readonly Dictionary<Guid, Guid?> _departmentParents = new();
    private readonly HashSet<Guid> _inactiveDepartments = new();
    private readonly Dictionary<Guid, Guid> _managerOf = new(); // employeeId -> direct manager employeeId
    private readonly HashSet<(Guid UserId, string PermissionCode)> _permissions = new();

    public DomainEmployee AddEmployee(
        Guid legalEntityId, Guid? departmentId = null, bool active = true, Guid? userId = null,
        Guid? tenantIdOverride = null)
    {
        var employee = new DomainEmployee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantIdOverride ?? TenantId,
            UserId = userId ?? Guid.NewGuid(),
            EmployeeNumber = $"E-{_employees.Count + 1}",
            FirstName = "First",
            LastName = "Last",
            Email = $"{Guid.NewGuid():N}@example.com",
            DepartmentId = departmentId,
            LegalEntityId = legalEntityId,
            EmploymentStatusId = active ? 1 : 2,
            HireDate = DateOnly.FromDateTime(Now.Date),
        };
        _employees.Add(employee);
        return employee;
    }

    public DomainPosition AddPosition(Guid legalEntityId, Guid? reportsToPositionId = null, bool isActive = true)
    {
        var position = new DomainPosition
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            LegalEntityId = legalEntityId,
            Name = $"Position-{_positions.Count + 1}",
            ReportsToPositionId = reportsToPositionId,
            IsActive = isActive,
        };
        _positions.Add(position);
        return position;
    }

    public void AddPrimaryAssignment(
        Guid employeeId, Guid positionId, bool active = true, Guid? reportsToEmployeeId = null)
    {
        _assignments.Add(new DomainPositionAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = employeeId,
            PositionId = positionId,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = active ? PositionAssignmentStatus.Active : PositionAssignmentStatus.Ended,
            EffectiveFrom = DateOnly.FromDateTime(Now.Date),
            ReportsToEmployeeId = reportsToEmployeeId,
        });

        if (active && reportsToEmployeeId is { } managerId)
            _managerOf[employeeId] = managerId;
    }

    /// <summary>Direct-manager edge used to derive the transitive closure (ancestors/descendants)
    /// the same way EfEmployeeHierarchyClosureRepository.RebuildAsync derives it from
    /// PositionAssignment.ReportsToEmployeeId / Position.ReportsToPositionId - tests only need to
    /// state "who reports to whom", not hand-author closure rows at every depth.</summary>
    public void SetManager(Guid employeeId, Guid managerEmployeeId) => _managerOf[employeeId] = managerEmployeeId;

    public Guid AddDepartment(Guid? parentDepartmentId = null, bool active = true)
    {
        var id = Guid.NewGuid();
        _departmentParents[id] = parentDepartmentId;
        if (!active)
            _inactiveDepartments.Add(id);
        return id;
    }

    public void AddCoverage(
        Guid legalEntityId,
        Guid ownerPositionId,
        string coveredTargetType,
        Guid? coveredPositionId,
        Guid? coveredDepartmentId,
        int ownerOrder,
        bool active = true,
        Guid? responsibleEmployeeId = null)
    {
        _coverage.Add(new ManagementCoverageRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            LegalEntityId = legalEntityId,
            OwnerPositionId = ownerPositionId,
            CoveredTargetType = coveredTargetType,
            CoveredPositionId = coveredPositionId,
            CoveredDepartmentId = coveredDepartmentId,
            OwnerOrder = ownerOrder,
            Status = active ? ManagementCoverageRecord.StatusActive : ManagementCoverageRecord.StatusInactive,
            ResponsibleEmployeeId = responsibleEmployeeId,
        });
    }

    public void GrantPermission(Guid userId, string permissionCode) => _permissions.Add((userId, permissionCode));

    private IReadOnlyList<Guid> AncestorsOf(Guid employeeId)
    {
        var chain = new List<Guid>();
        var current = employeeId;
        var visited = new HashSet<Guid> { employeeId };
        while (_managerOf.TryGetValue(current, out var manager) && visited.Add(manager))
        {
            chain.Add(manager);
            current = manager;
        }
        return chain;
    }

    private IReadOnlyList<Guid> DescendantsOf(IReadOnlyCollection<Guid> ancestorIds)
    {
        var result = new HashSet<Guid>();
        var frontier = new Queue<Guid>(ancestorIds);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var kvp in _managerOf)
            {
                if (kvp.Value == current && result.Add(kvp.Key))
                    frontier.Enqueue(kvp.Key);
            }
        }
        return result.ToList();
    }

    private IReadOnlyList<Guid> DescendantDepartmentsOf(Guid departmentId)
    {
        var result = new HashSet<Guid>();
        var frontier = new Queue<Guid>();
        frontier.Enqueue(departmentId);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var kvp in _departmentParents)
            {
                if (kvp.Value == current && !_inactiveDepartments.Contains(kvp.Key) && result.Add(kvp.Key))
                    frontier.Enqueue(kvp.Key);
            }
        }
        return result.ToList();
    }

    public IEmployeeAuthorityResolver BuildResolver(Guid? currentTenantId = null)
    {
        var currentUser = new FakeCurrentUser(currentTenantId ?? TenantId);
        var clock = new FakeDateTimeProvider(Now);

        return new EmployeeAuthorityResolver(
            currentUser,
            clock,
            new FakeEmployeeRepository(this),
            new FakePositionAssignmentRepository(this),
            new FakePositionRepository(this),
            new FakeClosureRepository(this),
            new FakeDepartmentRepository(this),
            new FakePermissionRepository(this));
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(Guid tenantId) => TenantId = tenantId;
        public Guid UserId => Guid.Empty;
        public Guid TenantId { get; }
        public string Email => "actor@example.com";
        public IReadOnlyList<string> Permissions => Array.Empty<string>();
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public FakeDateTimeProvider(DateTimeOffset now)
        {
            UtcNow = now;
            Today = DateOnly.FromDateTime(now.Date);
        }
        public DateTimeOffset UtcNow { get; }
        public DateOnly Today { get; }
    }

    private sealed class FakeEmployeeRepository : IEmployeeRepository
    {
        private readonly EmployeeAuthorityTestGraph _graph;
        public FakeEmployeeRepository(EmployeeAuthorityTestGraph graph) => _graph = graph;

        public Task<DomainEmployee?> GetByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
            => Task.FromResult(_graph._employees.FirstOrDefault(e => e.TenantId == tenantId && e.Id == employeeId));

        public Task<DomainEmployee?> GetByUserAndLegalEntityAsync(
            Guid tenantId, Guid userId, Guid legalEntityId, CancellationToken ct = default)
            => Task.FromResult(_graph._employees.FirstOrDefault(e =>
                e.TenantId == tenantId && e.UserId == userId && e.LegalEntityId == legalEntityId
                && e.EmploymentStatusId == 1));

        public Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsAsync(
            Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid>? departmentIds, CancellationToken ct = default)
        {
            if (departmentIds is not null && departmentIds.Count == 0)
                return Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

            var query = _graph._employees.Where(e =>
                e.TenantId == tenantId && e.LegalEntityId == legalEntityId && e.EmploymentStatusId == 1);

            if (departmentIds is not null)
                query = query.Where(e => e.DepartmentId is { } deptId && departmentIds.Contains(deptId));

            return Task.FromResult<IReadOnlyList<Guid>>(query.Select(e => e.Id).ToList());
        }

        public Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsByIdsAsync(
            Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
        {
            var result = _graph._employees
                .Where(e => e.TenantId == tenantId && e.LegalEntityId == legalEntityId
                    && e.EmploymentStatusId == 1 && employeeIds.Contains(e.Id))
                .Select(e => e.Id)
                .ToList();
            return Task.FromResult<IReadOnlyList<Guid>>(result);
        }

        public Task<(IReadOnlyList<EmployeeListItemResponse> Items, int TotalCount)> ListVisibleAsync(
            Guid tenantId, EmployeeVisibilityScope scope, EmployeeListFilter filter, int page, int pageSize,
            CancellationToken ct = default, EmployeeListAttendanceOptions? attendanceOptions = null) => throw new NotImplementedException();
        public Task<EmployeeListItemResponse?> GetVisibleByIdAsync(
            Guid tenantId, EmployeeVisibilityScope scope, Guid employeeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<EmployeeListItemResponse>> ListInvitedPendingByInviterAsync(
            Guid tenantId, Guid inviterUserId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DomainEmployee?> GetDefaultForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<DomainEmployee?> GetTrackedByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<uint?> GetVersionTokenAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public void SetExpectedVersion(DomainEmployee employee, string expectedVersion)
            => throw new NotImplementedException();
        public Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> EmployeeExistsInLegalEntityAsync(
            Guid tenantId, Guid legalEntityId, string email, Guid? excludeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> EmployeeNumberExistsAsync(Guid tenantId, string employeeNumber, Guid? excludeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> GetNextEmployeeNumberSequenceAsync(Guid tenantId, string prefix, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAsync(DomainEmployee employee, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakePositionAssignmentRepository : IPositionAssignmentRepository
    {
        private readonly EmployeeAuthorityTestGraph _graph;
        public FakePositionAssignmentRepository(EmployeeAuthorityTestGraph graph) => _graph = graph;

        public Task<DomainPositionAssignment?> GetActivePrimaryAsync(
            Guid tenantId, Guid employeeId, CancellationToken ct = default)
            => Task.FromResult(_graph._assignments.FirstOrDefault(a =>
                a.TenantId == tenantId && a.EmployeeId == employeeId
                && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && a.AssignmentStatus == PositionAssignmentStatus.Active));

        public Task<IReadOnlyList<PositionActiveHolder>> GetActiveHoldersAsync(
            Guid tenantId, Guid positionId, CancellationToken ct = default)
        {
            var holders = _graph._assignments
                .Where(a => a.TenantId == tenantId && a.PositionId == positionId
                    && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                    && a.AssignmentStatus == PositionAssignmentStatus.Active)
                .Join(_graph._employees, a => a.EmployeeId, e => e.Id,
                    (a, e) => new PositionActiveHolder(e.Id, e.FirstName, e.LastName, e.Email, e.AvatarFileId))
                .ToList();
            return Task.FromResult<IReadOnlyList<PositionActiveHolder>>(holders);
        }

        public Task<int> CountActiveAsync(Guid tenantId, Guid positionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, PositionOccupancyPreview>> GetOccupancyPreviewsAsync(
            Guid tenantId, IReadOnlyCollection<Guid> positionIds, int previewLimit, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> HasActivePrimaryInLegalEntityAsync(
            Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Guid?> TryReservePositionAssignmentAsync(
            Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById,
            Guid? reportsToEmployeeId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ActivatePlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> CancelPlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Guid?> TryCreateActiveAssignmentAsync(
            Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById,
            Guid? reportsToEmployeeId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ChecklistAssignee>> GetChecklistAssigneesAsync(
            Guid tenantId, Guid positionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> EndActiveAsync(Guid tenantId, Guid positionAssignmentId, DateOnly effectiveTo, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task AddAsync(DomainPositionAssignment assignment, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<DomainPositionAssignment?> GetTrackedAsync(Guid tenantId, Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainPositionAssignment>> ListHistoryForEmployeeAsync(
            Guid tenantId, Guid employeeId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakePositionRepository : IPositionRepository
    {
        private readonly EmployeeAuthorityTestGraph _graph;
        public FakePositionRepository(EmployeeAuthorityTestGraph graph) => _graph = graph;

        public Task<DomainPosition?> GetByIdAsync(Guid tenantId, Guid positionId, CancellationToken ct = default)
            => Task.FromResult(_graph._positions.FirstOrDefault(p => p.TenantId == tenantId && p.Id == positionId));

        // Mirrors EfPositionRepository.ListCoverageByOwnerPositionAsync exactly: no Status filter
        // (it backs the coverage-management UI, which shows inactive rows too) - the resolver is
        // responsible for filtering to active itself.
        public Task<IReadOnlyList<ManagementCoverageRecord>> ListCoverageByOwnerPositionAsync(
            Guid tenantId, Guid legalEntityId, Guid ownerPositionId, CancellationToken ct = default)
        {
            var rows = _graph._coverage
                .Where(c => c.TenantId == tenantId && c.LegalEntityId == legalEntityId
                    && c.OwnerPositionId == ownerPositionId)
                .OrderBy(c => c.OwnerOrder)
                .ToList();
            return Task.FromResult<IReadOnlyList<ManagementCoverageRecord>>(rows);
        }

        public Task<IReadOnlyList<ManagementCoverageRecord>> ListActiveCoverageByCoveredTargetAsync(
            Guid tenantId, Guid legalEntityId, string coveredTargetType, Guid? coveredPositionId,
            Guid? coveredDepartmentId, Guid? excludingRecordId = null, CancellationToken ct = default)
        {
            var rows = _graph._coverage
                .Where(c => c.TenantId == tenantId && c.LegalEntityId == legalEntityId
                    && c.CoveredTargetType == coveredTargetType
                    && c.CoveredPositionId == coveredPositionId
                    && c.CoveredDepartmentId == coveredDepartmentId
                    && c.Status == ManagementCoverageRecord.StatusActive
                    && (excludingRecordId == null || c.Id != excludingRecordId))
                .OrderBy(c => c.OwnerOrder)
                .ToList();
            return Task.FromResult<IReadOnlyList<ManagementCoverageRecord>>(rows);
        }

        public Task<DomainPosition?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAsync(DomainPosition position, CancellationToken ct = default) => throw new NotImplementedException();
        public void Update(DomainPosition position) => throw new NotImplementedException();
        public Task<DomainPosition?> GetByIdForLegalEntityAsync(Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainPosition>> GetByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainPosition>> ListByLegalEntityAsync(
            Guid tenantId, Guid legalEntityId, bool includeInactive = false, Guid? departmentId = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> ExistsByCodeAsync(
            Guid tenantId, Guid legalEntityId, string code, Guid? excludingPositionId = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> ExistsByNameAsync(
            Guid tenantId, Guid legalEntityId, string name, Guid? excludingPositionId = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> ExistsInDepartmentAsync(
            Guid tenantId, Guid legalEntityId, Guid departmentId, Guid positionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> IsDescendantAsync(
            Guid tenantId, Guid legalEntityId, Guid positionId, Guid possibleDescendantId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> CountActiveByDepartmentAsync(Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, int>> CountActiveByDepartmentIdsAsync(
            Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> departmentIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> CountActiveReportsToPositionAsync(Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PositionPage> ListPageAsync(
            Guid tenantId, Guid legalEntityId, Guid? departmentId, string? search, bool includeInactive,
            string sortBy, string sortDirection, int page, int pageSize, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> CountHeadDepartmentReferencesAsync(Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task AddReportingHistoryAsync(PositionReportingHistory history, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task AddManagementCoverageRecordAsync(ManagementCoverageRecord record, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PositionReportingHistory?> GetCurrentReportingHistoryAsync(Guid tenantId, Guid positionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public void UpdateReportingHistory(PositionReportingHistory history) => throw new NotImplementedException();
        public Task<ManagementCoverageRecord?> GetLockedReportingStructureCoverageAsync(
            Guid tenantId, Guid ownerPositionId, Guid coveredPositionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ManagementCoverageRecord?> GetCoverageRecordByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();
        public void RemoveCoverageRecord(ManagementCoverageRecord record) => throw new NotImplementedException();
        public void UpdateCoverageRecord(ManagementCoverageRecord record) => throw new NotImplementedException();
        public Task<bool> HasActiveCoverageConflictAsync(
            Guid tenantId, Guid legalEntityId, string coveredTargetType, Guid? coveredPositionId,
            Guid? coveredDepartmentId, int ownerOrder, Guid? excludingRecordId = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PositionAccessTemplate?> GetAccessTemplateByPositionAsync(Guid tenantId, Guid positionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PositionAccessTemplate?> GetAccessTemplateByPositionIncludingInactiveAsync(Guid tenantId, Guid positionId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, bool>> GetRequiresApprovalByPositionIdsAsync(
            Guid tenantId, IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAccessTemplateAsync(PositionAccessTemplate template, CancellationToken ct = default) => throw new NotImplementedException();
        public void UpdateAccessTemplate(PositionAccessTemplate template) => throw new NotImplementedException();
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeClosureRepository : IEmployeeHierarchyClosureRepository
    {
        private readonly EmployeeAuthorityTestGraph _graph;
        public FakeClosureRepository(EmployeeAuthorityTestGraph graph) => _graph = graph;

        public Task<IReadOnlyList<Guid>> GetDescendantEmployeeIdsAsync(
            Guid tenantId, IReadOnlyCollection<Guid> ancestorEmployeeIds, CancellationToken ct = default)
            => Task.FromResult(_graph.DescendantsOf(ancestorEmployeeIds));

        public Task<IReadOnlyList<Guid>> GetAncestorChainEmployeeIdsAsync(
            Guid tenantId, Guid employeeId, CancellationToken ct = default)
            => Task.FromResult(_graph.AncestorsOf(employeeId));

        public Task RebuildAsync(Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> GetDirectReportEmployeeIdsAsync(
            Guid tenantId, Guid managerEmployeeId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Guid?> GetDirectManagerEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeDepartmentRepository : IDepartmentRepository
    {
        private readonly EmployeeAuthorityTestGraph _graph;
        public FakeDepartmentRepository(EmployeeAuthorityTestGraph graph) => _graph = graph;

        public Task<IReadOnlyList<Guid>> GetDescendantDepartmentIdsAsync(
            Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
            => Task.FromResult(_graph.DescendantDepartmentsOf(departmentId)
                .Where(id => id != departmentId).ToList() as IReadOnlyList<Guid>);

        public Task<IReadOnlyList<ONEVO.Domain.Features.OrgStructure.Entities.Department>> ListByLegalEntityAsync(
            Guid tenantId, Guid legalEntityId, bool includeInactive, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<DepartmentPage> ListPageByLegalEntityAsync(
            Guid tenantId, Guid legalEntityId, string? search, bool includeInactive, Guid? parentDepartmentId,
            DepartmentSortBy sortBy, SortDirection sortDirection, int page, int pageSize, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<ONEVO.Domain.Features.OrgStructure.Entities.Department>> ListForTreeByLegalEntityAsync(
            Guid tenantId, Guid legalEntityId, string? search, bool includeInactive, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ONEVO.Domain.Features.OrgStructure.Entities.Department?> GetByIdAsync(
            Guid tenantId, Guid departmentId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ONEVO.Domain.Features.OrgStructure.Entities.Department?> GetByIdForLegalEntityAsync(
            Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> ExistsByNameAsync(
            Guid tenantId, Guid legalEntityId, string name, Guid? excludingDepartmentId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> ExistsByCodeAsync(
            Guid tenantId, Guid legalEntityId, string code, Guid? excludingDepartmentId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> ExistsAsync(Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> IsDescendantAsync(
            Guid tenantId, Guid legalEntityId, Guid departmentId, Guid possibleDescendantId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> CountActiveChildrenAsync(Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> CountActiveEmployeesAsync(Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, int>> CountActiveEmployeesByDepartmentIdsAsync(
            Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> departmentIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task AddAsync(ONEVO.Domain.Features.OrgStructure.Entities.Department department, CancellationToken ct = default)
            => throw new NotImplementedException();
        public void Update(ONEVO.Domain.Features.OrgStructure.Entities.Department department) => throw new NotImplementedException();
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakePermissionRepository : IPermissionRepository
    {
        private readonly EmployeeAuthorityTestGraph _graph;
        public FakePermissionRepository(EmployeeAuthorityTestGraph graph) => _graph = graph;

        public Task<bool> UserHasPermissionCodeAsync(
            Guid userId, string permissionCode, DateTimeOffset now, CancellationToken ct = default)
            => Task.FromResult(_graph._permissions.Contains((userId, permissionCode)));

        public Task<AuthPermission?> GetByCodeAsync(string code, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AuthPermission>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<AuthPermission>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<PermissionCodeWithModule>> ListRolePermissionCodesWithModulesAsync(
            Guid userId, DateTimeOffset now, Guid? activeLegalEntityId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> ListUserIdsWithPermissionCodeAsync(
            Guid tenantId, string permissionCode, DateTimeOffset now, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
