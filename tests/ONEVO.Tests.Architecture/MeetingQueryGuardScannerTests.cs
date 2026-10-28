using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>The guard scans must catch the shapes a leaked meeting query actually takes, and stay quiet on prose.</summary>
public sealed class MeetingQueryGuardScannerTests
{
    [Fact]
    public void BareIgnore_ThroughSetOfWorkTask_IsFound()
        => Assert.Equal(["Load"], MeetingQueryGuardScanner.MembersWithBareWorkTaskIgnore("""
            public Task<List<WorkTask>> Load() => _db.Set<WorkTask>().IgnoreQueryFilters().ToListAsync();
            """));

    [Fact]
    public void BareIgnore_SplitAcrossStatements_IsFound()
        => Assert.Equal(["Load"], MeetingQueryGuardScanner.MembersWithBareWorkTaskIgnore("""
            public async Task<int> Load()
            {
                var query = _db.WorkTasks.AsQueryable();
                query = query.IgnoreQueryFilters();
                return await query.CountAsync();
            }
            public int Other() => 1;
            """));

    [Fact]
    public void NamedIgnore_OfTheTenantFilter_IsNotBare()
        => Assert.Empty(MeetingQueryGuardScanner.MembersWithBareWorkTaskIgnore("""
            public Task<WorkTask?> Load(Guid id) => _db.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.TenantScope]).FirstOrDefaultAsync();
            """));

    [Theory]
    [InlineData("var names = new[] { WorkTaskQueryFilters.ExcludeMeetings };")]
    [InlineData("query.IgnoreQueryFilters([ExcludeMeetings]); // using static WorkTaskQueryFilters")]
    [InlineData("query.IgnoreQueryFilters([\"ExcludeMeetings\"]);")]
    public void AnySpellingOfExcludeMeetings_IsFound(string source)
        => Assert.True(MeetingQueryGuardScanner.MentionsExcludeMeetings(source));

    [Theory]
    [InlineData("private const string Sql = \"SELECT id FROM tasks WHERE tenant_id = @t\";")]
    [InlineData("command.CommandText = \"UPDATE tasks SET title = @title\";")]
    [InlineData("var rows = await connection.QueryAsync<Guid>(\"SELECT t.id FROM projects p JOIN tasks t ON t.project_id = p.id\");")]
    [InlineData("await _db.Database.SqlQuery<Guid>($\"SELECT id FROM tasks\").ToListAsync();")]
    public void RawSqlOnTasks_InAnyCarrier_IsFound(string source)
        => Assert.True(MeetingQueryGuardScanner.HasRawSqlOnTasks(source));

    [Theory]
    [InlineData("return Result.Conflict(\"Move all tasks out of this status before deleting it.\");")]
    [InlineData("// copies the tasks from the template into the project")]
    [InlineData("await _db.Database.ExecuteSqlInterpolatedAsync($\"UPDATE task_statuses SET name = {name}\");")]
    public void Prose_AndOtherTables_AreNotRawSqlOnTasks(string source)
        => Assert.False(MeetingQueryGuardScanner.HasRawSqlOnTasks(source));
}
