using System.Linq.Expressions;

namespace ONEVO.Infrastructure.Persistence.SqlServer;

/// <summary>
/// SQL Server sorts NULLs first ascending and last descending, and has no NULLS LAST. These keep
/// the order the API has always returned: NULLs last ascending, first descending.
/// </summary>
public static class NullOrdering
{
    public static IOrderedQueryable<T> OrderByNullsLast<T, TKey>(
        this IQueryable<T> query, Expression<Func<T, TKey>> key)
        => query.OrderBy(IsNull(key)).ThenBy(key);

    public static IOrderedQueryable<T> OrderByDescendingNullsFirst<T, TKey>(
        this IQueryable<T> query, Expression<Func<T, TKey>> key)
        => query.OrderByDescending(IsNull(key)).ThenByDescending(key);

    private static Expression<Func<T, bool>> IsNull<T, TKey>(Expression<Func<T, TKey>> key)
        => Expression.Lambda<Func<T, bool>>(
            Expression.Equal(key.Body, Expression.Constant(null, key.Body.Type)), key.Parameters);
}
