using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Tests.Unit.Features.CoreHr.EmployeeAuthority;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

/// <summary>
/// Runs ListEmployeesQueryHandler against a *real* EmployeeAuthorityResolver (built from the
/// same EmployeeAuthorityTestGraph fakes EMPLOYEE_AUTHORITY_RESOLVER_BACKEND_PART0 uses), paired
/// with a small purpose-built IEmployeeRepository fake that implements the two methods the
/// resolver-mocked ListEmployeesQueryHandlerTests.cs stubs out: ListVisibleAsync (applying
/// EmployeeListFilter.RestrictToEmployeeIds exactly the way EfEmployeeRepository does) and
/// ListInvitedPendingByInviterAsync. Nothing in this file mocks the resolver itself - this is the
/// closest thing to an integration test the current test infrastructure supports without Docker
/// (Testcontainers/PostgreSQL are unavailable in this environment, per the Part 0 report §10).
/// </summary>
public sealed class ListEmployeesQueryHandlerAuthorityResolverIntegrationTests
{
    [Fact]
    public async Task Handle_Sees_TransitivePositionCoverage_ThroughRealResolver()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var ceoPosition = graph.AddPosition(legalEntityId);
        var gmPosition = graph.AddPosition(legalEntityId, ceoPosition.Id);
        var pmPosition = graph.AddPosition(legalEntityId, gmPosition.Id);
        var engineerPosition = graph.AddPosition(legalEntityId, pmPosition.Id);

        var actor = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, ceoPosition.Id);
        graph.GrantPermission(actor.UserId, "employees:read");

        var gm = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(gm.Id, gmPosition.Id, reportsToEmployeeId: actor.Id);
        var pm = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(pm.Id, pmPosition.Id, reportsToEmployeeId: gm.Id);
        var engineer = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(engineer.Id, engineerPosition.Id, reportsToEmployeeId: pm.Id);

        // CEO's own position only directly covers GM's position - Engineer is two levels below
        // the covered position, reachable only through transitive closure expansion.
        graph.AddCoverage(legalEntityId, ceoPosition.Id, "Position", gmPosition.Id, null, ownerOrder: 1);

        var repo = new FakeListEmployeesRepository();
        repo.Employees[actor.Id] = ListItem(actor, legalEntityId);
        repo.Employees[gm.Id] = ListItem(gm, legalEntityId);
        repo.Employees[pm.Id] = ListItem(pm, legalEntityId);
        repo.Employees[engineer.Id] = ListItem(engineer, legalEntityId);
        repo.DefaultEmployeeByUser[actor.UserId] = actor;

        var currentUser = FakeCurrentUser(graph.TenantId, actor.UserId);
        var handler = new ListEmployeesQueryHandler(repo, graph.BuildResolver(), currentUser, graph.Clock);

        var result = await handler.Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var ids = result.Value!.Items.Select(i => i.Id).ToHashSet();
        Assert.Contains(actor.Id, ids); // self
        Assert.Contains(gm.Id, ids); // direct coverage
        Assert.Contains(pm.Id, ids); // transitive
        Assert.Contains(engineer.Id, ids); // transitive
    }

    [Fact]
    public async Task Handle_Sees_ManualCoverageOwner_OutsideReportingLine_ThroughRealResolver()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var hrBpPosition = graph.AddPosition(legalEntityId);
        var actor = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, hrBpPosition.Id);
        graph.GrantPermission(actor.UserId, "employees:read");

        // Covered department has no reporting-line relationship to the HR business partner at all.
        var coveredDepartmentId = graph.AddDepartment();
        var coveredEmployee = graph.AddEmployee(legalEntityId, coveredDepartmentId);

        graph.AddCoverage(legalEntityId, hrBpPosition.Id, "Department", null, coveredDepartmentId, ownerOrder: 1);

        var repo = new FakeListEmployeesRepository();
        repo.Employees[actor.Id] = ListItem(actor, legalEntityId);
        repo.Employees[coveredEmployee.Id] = ListItem(coveredEmployee, legalEntityId);
        repo.DefaultEmployeeByUser[actor.UserId] = actor;

        var currentUser = FakeCurrentUser(graph.TenantId, actor.UserId);
        var handler = new ListEmployeesQueryHandler(repo, graph.BuildResolver(), currentUser, graph.Clock);

        var result = await handler.Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.Contains(result.Value!.Items, i => i.Id == coveredEmployee.Id);
    }

    [Fact]
    public async Task Handle_Excludes_EmployeeOutsideVisibleIds()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var actor = graph.AddEmployee(legalEntityId);
        var unrelated = graph.AddEmployee(legalEntityId);

        var repo = new FakeListEmployeesRepository();
        repo.Employees[actor.Id] = ListItem(actor, legalEntityId);
        repo.Employees[unrelated.Id] = ListItem(unrelated, legalEntityId);
        repo.DefaultEmployeeByUser[actor.UserId] = actor;

        var currentUser = FakeCurrentUser(graph.TenantId, actor.UserId);
        var handler = new ListEmployeesQueryHandler(repo, graph.BuildResolver(), currentUser, graph.Clock);

        var result = await handler.Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        var ids = result.Value!.Items.Select(i => i.Id).ToHashSet();
        Assert.Contains(actor.Id, ids); // self
        Assert.DoesNotContain(unrelated.Id, ids); // no coverage, no permission, not self
    }

    [Fact]
    public async Task Handle_Excludes_ActorWithoutPermissionAndWithoutSelf()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var other = graph.AddEmployee(legalEntityId);

        var repo = new FakeListEmployeesRepository();
        repo.Employees[other.Id] = ListItem(other, legalEntityId);
        // Actor has no Employee row at all in this legal entity and no permission.
        repo.DefaultEmployeeByUser[actorUserId] = null;

        var currentUser = FakeCurrentUser(graph.TenantId, actorUserId);
        var handler = new ListEmployeesQueryHandler(repo, graph.BuildResolver(), currentUser, graph.Clock);

        var result = await handler.Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task Handle_Excludes_CrossLegalEntityEmployee_EvenWithCompanyWideCoverage()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();

        var actorPosition = graph.AddPosition(legalEntityId);
        var actor = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        graph.GrantPermission(actor.UserId, "employees:read");
        graph.AddCoverage(legalEntityId, actorPosition.Id, "Company", null, null, ownerOrder: 1);

        var inLegalEntity = graph.AddEmployee(legalEntityId);
        var inOtherLegalEntity = graph.AddEmployee(otherLegalEntityId);

        var repo = new FakeListEmployeesRepository();
        repo.Employees[actor.Id] = ListItem(actor, legalEntityId);
        repo.Employees[inLegalEntity.Id] = ListItem(inLegalEntity, legalEntityId);
        repo.Employees[inOtherLegalEntity.Id] = ListItem(inOtherLegalEntity, otherLegalEntityId);
        repo.DefaultEmployeeByUser[actor.UserId] = actor;

        var currentUser = FakeCurrentUser(graph.TenantId, actor.UserId);
        var handler = new ListEmployeesQueryHandler(repo, graph.BuildResolver(), currentUser, graph.Clock);

        var result = await handler.Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        var ids = result.Value!.Items.Select(i => i.Id).ToHashSet();
        Assert.Contains(inLegalEntity.Id, ids);
        Assert.DoesNotContain(inOtherLegalEntity.Id, ids);
    }

    [Fact]
    public async Task Handle_Excludes_CrossTenantEmployee()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var actorPosition = graph.AddPosition(legalEntityId);
        var actor = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        graph.GrantPermission(actor.UserId, "employees:read");
        graph.AddCoverage(legalEntityId, actorPosition.Id, "Company", null, null, ownerOrder: 1);

        var inTenant = graph.AddEmployee(legalEntityId);
        var inOtherTenant = graph.AddEmployee(legalEntityId, tenantIdOverride: otherTenantId);

        var repo = new FakeListEmployeesRepository();
        repo.Employees[actor.Id] = ListItem(actor, legalEntityId);
        repo.Employees[inTenant.Id] = ListItem(inTenant, legalEntityId);
        repo.Employees[inOtherTenant.Id] = ListItem(inOtherTenant, legalEntityId);
        repo.DefaultEmployeeByUser[actor.UserId] = actor;

        var currentUser = FakeCurrentUser(graph.TenantId, actor.UserId);
        var handler = new ListEmployeesQueryHandler(repo, graph.BuildResolver(), currentUser, graph.Clock);

        var result = await handler.Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        var ids = result.Value!.Items.Select(i => i.Id).ToHashSet();
        Assert.Contains(inTenant.Id, ids);
        Assert.DoesNotContain(inOtherTenant.Id, ids);
    }

    [Fact]
    public async Task Handle_AppliesSearchFilter_WithinResolverVisibleIds()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var actorPosition = graph.AddPosition(legalEntityId);
        var actor = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        graph.GrantPermission(actor.UserId, "employees:read");
        graph.AddCoverage(legalEntityId, actorPosition.Id, "Company", null, null, ownerOrder: 1);

        var ada = graph.AddEmployee(legalEntityId);
        var bob = graph.AddEmployee(legalEntityId);

        var repo = new FakeListEmployeesRepository();
        repo.Employees[actor.Id] = ListItem(actor, legalEntityId, "Actor");
        repo.Employees[ada.Id] = ListItem(ada, legalEntityId, "Ada");
        repo.Employees[bob.Id] = ListItem(bob, legalEntityId, "Bob");
        repo.DefaultEmployeeByUser[actor.UserId] = actor;

        var currentUser = FakeCurrentUser(graph.TenantId, actor.UserId);
        var handler = new ListEmployeesQueryHandler(repo, graph.BuildResolver(), currentUser, graph.Clock);

        var result = await handler.Handle(new ListEmployeesQuery("ada", null, null), CancellationToken.None);

        var ids = result.Value!.Items.Select(i => i.Id).ToHashSet();
        Assert.Contains(ada.Id, ids);
        Assert.DoesNotContain(bob.Id, ids);
        Assert.DoesNotContain(actor.Id, ids);
    }

    private static EmployeeListItemResponse ListItem(EmployeeEntity employee, Guid legalEntityId, string? name = null) => new(
        employee.Id, employee.EmployeeNumber, name ?? $"{employee.FirstName} {employee.LastName}", employee.Email,
        employee.DepartmentId, null, null, null, legalEntityId, null, "Full-Time", "active", null, null);

    private static ICurrentUser FakeCurrentUser(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ICurrentUser>();
        mock.SetupGet(u => u.TenantId).Returns(tenantId);
        mock.SetupGet(u => u.UserId).Returns(userId);
        return mock.Object;
    }

    /// <summary>Minimal IEmployeeRepository fake that mirrors EfEmployeeRepository.ListVisibleAsync's
    /// RestrictToEmployeeIds handling in-memory, so this test proves the resolver's output actually
    /// flows through the handler into the repository filter, end to end.</summary>
    private sealed class FakeListEmployeesRepository : IEmployeeRepository
    {
        public Dictionary<Guid, EmployeeListItemResponse> Employees { get; } = new();
        public Dictionary<Guid, EmployeeEntity?> DefaultEmployeeByUser { get; } = new();

        public Task<EmployeeEntity?> GetDefaultForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => Task.FromResult(DefaultEmployeeByUser.TryGetValue(userId, out var e) ? e : null);

        public Task<(IReadOnlyList<EmployeeListItemResponse> Items, int TotalCount)> ListVisibleAsync(
            Guid tenantId, EmployeeVisibilityScope scope, EmployeeListFilter filter, int page, int pageSize,
            CancellationToken ct = default, EmployeeListAttendanceOptions? attendanceOptions = null)
        {
            IEnumerable<EmployeeListItemResponse> query = Employees.Values;

            if (filter.RestrictToEmployeeIds is not null)
                query = query.Where(e => filter.RestrictToEmployeeIds.Contains(e.Id));

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var normalized = filter.Search.Trim().ToLowerInvariant();
                query = query.Where(e => e.FullName.ToLowerInvariant().Contains(normalized));
            }

            if (filter.LegalEntityId is not null)
                query = query.Where(e => e.LegalEntityId == filter.LegalEntityId);

            var list = query.OrderBy(e => e.FullName).ToList();
            return Task.FromResult<(IReadOnlyList<EmployeeListItemResponse>, int)>((list, list.Count));
        }

        public Task<IReadOnlyList<EmployeeListItemResponse>> ListInvitedPendingByInviterAsync(
            Guid tenantId, Guid inviterUserId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeeListItemResponse>>(Array.Empty<EmployeeListItemResponse>());

        public Task<EmployeeListItemResponse?> GetVisibleByIdAsync(
            Guid tenantId, EmployeeVisibilityScope scope, Guid employeeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<EmployeeEntity?> GetByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<EmployeeEntity?> GetByUserAndLegalEntityAsync(Guid tenantId, Guid userId, Guid legalEntityId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<EmployeeEntity?> GetTrackedByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public void SetExpectedVersion(EmployeeEntity employee, string expectedVersion) => throw new NotImplementedException();
        public Task<uint?> GetVersionTokenAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> EmployeeExistsInLegalEntityAsync(Guid tenantId, Guid legalEntityId, string email, Guid? excludeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> EmployeeNumberExistsAsync(Guid tenantId, string employeeNumber, Guid? excludeId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> GetNextEmployeeNumberSequenceAsync(Guid tenantId, string prefix, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsAsync(
            Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid>? departmentIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsByIdsAsync(
            Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task AddAsync(EmployeeEntity employee, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }
}
