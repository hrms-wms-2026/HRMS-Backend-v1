using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ONEVO.Infrastructure.Persistence.SqlServer;

/// <summary>
/// JSON text stored in nvarchar(max). A check constraint keeps the rule PostgreSQL's jsonb type
/// enforced: a column can only ever hold valid JSON.
/// </summary>
public static class JsonColumnConvention
{
    private const string Annotation = "Crownus:JsonColumn";

    public static PropertyBuilder<T> IsJsonColumn<T>(this PropertyBuilder<T> property)
        => property.HasAnnotation(Annotation, true);

    /// <summary>Adds ck_&lt;table&gt;_&lt;column&gt;_json for every column marked with IsJsonColumn.</summary>
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(e => e.GetTableName() is not null))
        {
            foreach (var property in entity.GetProperties().Where(p => p.FindAnnotation(Annotation) is not null))
            {
                var table = entity.GetTableName();
                var column = property.GetColumnName();
                entity.AddCheckConstraint($"ck_{table}_{column}_json", $"ISJSON([{column}]) = 1");
            }
        }
    }
}
