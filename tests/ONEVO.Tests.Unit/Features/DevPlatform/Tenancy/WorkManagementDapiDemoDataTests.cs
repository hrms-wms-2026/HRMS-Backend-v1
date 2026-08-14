using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public class WorkManagementDapiDemoDataTests
{
    [Fact]
    public void Persons_Has22UniqueKeysEmailsAndEmployeeNumbers()
    {
        Assert.Equal(22, WorkManagementDapiDemoData.Persons.Count);
        Assert.Equal(22, WorkManagementDapiDemoData.Persons.Select(p => p.Key).Distinct().Count());
        Assert.Equal(22, WorkManagementDapiDemoData.Persons.Select(p => p.Email).Distinct().Count());
        Assert.Equal(22, WorkManagementDapiDemoData.Persons.Select(p => p.EmployeeNumber).Distinct().Count());
        Assert.All(WorkManagementDapiDemoData.Persons, p => Assert.EndsWith("@dapi.test", p.Email));
    }

    [Fact]
    public void ProjectTrees_Has5ProjectsWithUniqueIdentifiers()
    {
        Assert.Equal(5, WorkManagementDapiDemoData.ProjectTrees.Count);
        Assert.Equal(5, WorkManagementDapiDemoData.ProjectTrees.Select(t => t.Identifier).Distinct().Count());
        Assert.Equal(5, WorkManagementDapiDemoData.ProjectTrees.Select(t => t.ProjectKey).Distinct().Count());
    }

    [Theory]
    [InlineData("epos")]
    [InlineData("evtix")]
    [InlineData("onexso")]
    [InlineData("watercraft")]
    [InlineData("hwportal")]
    public void EveryProjectTree_ReachesExactlyFiveLayersDeep(string projectKey)
    {
        var tree = WorkManagementDapiDemoData.ProjectTrees.Single(t => t.ProjectKey == projectKey);

        var maxDepth = MaxDepth(tree.Root, 1);

        Assert.Equal(5, maxDepth);
    }

    [Fact]
    public void EveryOwnerKeyAndExtraMemberKey_ExistsInRoster()
    {
        var knownKeys = new HashSet<string>(WorkManagementDapiDemoData.PersonsByKey.Keys) { "dabi" };

        foreach (var tree in WorkManagementDapiDemoData.ProjectTrees)
        {
            AssertKeysKnown(tree.Root, knownKeys);
        }
    }

    private static int MaxDepth(DemoObjectiveNode node, int depth)
        => node.Children.Length == 0 ? depth : node.Children.Max(c => MaxDepth(c, depth + 1));

    private static void AssertKeysKnown(DemoObjectiveNode node, HashSet<string> knownKeys)
    {
        Assert.Contains(node.OwnerKey, knownKeys);
        foreach (var extra in node.ExtraMemberKeys)
        {
            Assert.Contains(extra, knownKeys);
        }
        foreach (var child in node.Children)
        {
            AssertKeysKnown(child, knownKeys);
        }
    }
}
