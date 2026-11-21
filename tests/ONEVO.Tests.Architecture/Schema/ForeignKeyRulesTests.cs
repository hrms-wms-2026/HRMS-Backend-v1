using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ONEVO.Tests.Architecture.Schema;

/// <summary>Task 39 rules for foreign keys, checked on the EF model.</summary>
public sealed class ForeignKeyRulesTests
{
    /// <summary>Every foreign key needs an index (or key) whose leading columns are the key's columns,
    /// so the check on a parent delete and joins on the key do not scan the child table.</summary>
    [Fact]
    public void EveryForeignKeyHasAnIndexThatStartsWithIt()
    {
        var offenders = new List<string>();
        foreach (var entity in SchemaIntegrityInventory.Model().GetEntityTypes().Where(e => e.GetTableName() is not null))
        {
            var leading = entity.GetIndexes().Select(index => index.Properties)
                .Concat(entity.GetKeys().Select(key => key.Properties))
                .ToList();
            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var covered = leading.Any(columns => columns.Count >= foreignKey.Properties.Count
                    && columns.Take(foreignKey.Properties.Count).SequenceEqual(foreignKey.Properties));
                if (!covered)
                    offenders.Add($"{foreignKey.GetConstraintName()} ({string.Join(", ", foreignKey.Properties.Select(p => p.Name))})");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>A key to a person (employees, users) never cascades: removing a person must not delete
    /// their history.</summary>
    [Fact]
    public void NoForeignKeyToAPersonCascades()
    {
        string[] people = ["employees", "users"];
        var offenders = SchemaIntegrityInventory.Model().GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(foreignKey => people.Contains(foreignKey.PrincipalEntityType.GetTableName())
                && foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade)
            .Select(foreignKey => foreignKey.GetConstraintName())
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
