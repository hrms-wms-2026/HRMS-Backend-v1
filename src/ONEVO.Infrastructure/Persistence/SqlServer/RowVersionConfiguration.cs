using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ONEVO.Infrastructure.Persistence.SqlServer;

/// <summary>
/// Task 42: the optimistic-concurrency token of a table users edit through forms. A SQL Server rowversion column named
/// xmin (the name every older token uses, from PostgreSQL), mapped as a nullable shadow property so the in-process unit-test
/// schema accepts inserts. A save whose expected version (<see cref="RowVersionToken"/>, sent back by the client) no
/// longer matches fails with DbUpdateConcurrencyException, which the API returns as 409.
/// </summary>
public static class RowVersionConfiguration
{
    public const string PropertyName = "xmin";

    /// <summary>Marks the tokens added by task 42: a detached update (no version known) reads the current version first
    /// (ApplicationDbContext.SaveChangesAsync), so code that updates a row it loaded without tracking keeps working.</summary>
    public const string FillDetachedAnnotation = "Crownus:RowVersion:FillDetached";

    public static EntityTypeBuilder<TEntity> HasRowVersionToken<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<ulong?>(PropertyName)
            .HasColumnName(PropertyName)
            .IsRowVersion()
            .HasAnnotation(FillDetachedAnnotation, true);
        return builder;
    }
}
