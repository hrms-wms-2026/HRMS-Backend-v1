using System.Collections;
using System.Data.Common;
using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using Xunit.Abstractions;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// LINQ that SQL Server can't translate, and PostgreSQL-only raw SQL, only fail when the query
/// runs. This calls every public async method of every Infrastructure class that takes
/// <see cref="ApplicationDbContext"/>, with generated arguments, against one throwaway SQL Server
/// database under a tenant session (RLS on). Each call gets a fresh context.
///
/// Outcomes:
/// - Ran: the call issued at least one command and completed, or failed with an expected data
///   error (foreign key, unique, NOT NULL, RLS block) or a domain/argument exception.
/// - Failed: a SQL error outside that list, or an exception from EF's query pipeline. These fail
///   the test.
/// - Unreached: no command was issued (an early return or a guard). Not a pass: these are listed
///   and need a focused test or a skip-list reason.
///
/// A green run proves only the calls that ran. Line coverage of the query call sites, measured
/// separately, decides whether the risk is closed.
/// </summary>
public sealed class QueryTranslationSweepTests
{
    // Expected data errors: FK or CHECK, unique index, unique constraint, NOT NULL, RLS block
    // predicate, string truncation, and 3621 ("the statement has been terminated", which SQL
    // Server sends alongside them).
    private static readonly HashSet<int> ExpectedSqlErrors = [547, 2601, 2627, 515, 33504, 2628, 8152, 3621];

    // Classes or methods that can't be called generically, each with a reason.
    private static readonly Dictionary<string, string> Skipped = new(StringComparer.Ordinal)
    {
    };

    private readonly ITestOutputHelper _output;

    public QueryTranslationSweepTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task EveryQueryMethod_RunsOnSqlServer()
    {
        var h = await MyTeamDb.CreateAsync();
        var results = new List<(string Name, string Outcome, string Detail)>();

        foreach (var type in QueryTypes())
        {
            foreach (var method in QueryMethods(type))
            {
                var name = $"{type.Name}.{method.Name}({string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name))})";
                if (Skipped.ContainsKey(type.Name) || Skipped.ContainsKey($"{type.Name}.{method.Name}"))
                {
                    results.Add((name, "skipped", Skipped.GetValueOrDefault(type.Name) ?? Skipped[$"{type.Name}.{method.Name}"]));
                    continue;
                }

                results.Add(await RunAsync(h, type, method, name));
            }
        }

        var report = new StringBuilder();
        foreach (var group in results.GroupBy(r => r.Outcome).OrderBy(g => g.Key))
            report.AppendLine($"{group.Key}: {group.Count()}");
        foreach (var r in results.Where(r => r.Outcome != "ran").OrderBy(r => r.Outcome).ThenBy(r => r.Name))
            report.AppendLine($"{r.Outcome}\t{r.Name}\t{r.Detail}");
        var text = report.ToString();
        var reportPath = Environment.GetEnvironmentVariable("QUERY_SWEEP_REPORT");
        if (!string.IsNullOrEmpty(reportPath))
            await File.WriteAllTextAsync(reportPath, text);
        _output.WriteLine(text);

        var failed = results.Where(r => r.Outcome == "failed").Select(r => $"{r.Name}: {r.Detail}").ToList();
        Assert.True(failed.Count == 0, $"{failed.Count} queries failed on SQL Server:\n{string.Join("\n", failed)}");
    }

    private static async Task<(string, string, string)> RunAsync(MyTeamDb h, Type type, MethodInfo method, string name)
    {
        var result = await RunOnceAsync(h, type, method, name, preTenant: false, inTransaction: false);
        // Some methods must run inside the caller's transaction, or before a tenant is resolved
        // (login lookups). Run those once more the way they're meant to be called.
        if (result.Outcome == "unreached" && result.Detail.Contains("transaction", StringComparison.Ordinal))
            result = await RunOnceAsync(h, type, method, name, preTenant: false, inTransaction: true);
        else if (result.Outcome == "unreached" && result.Detail.Contains("Pre-tenant", StringComparison.Ordinal))
            result = await RunOnceAsync(h, type, method, name, preTenant: true, inTransaction: false);
        return result;
    }

    private static async Task<(string Name, string Outcome, string Detail)> RunOnceAsync(
        MyTeamDb h, Type type, MethodInfo method, string name, bool preTenant, bool inTransaction)
    {
        var counter = new CommandCounter();
        await using var db = preTenant ? h.NewContext(counter) : h.NewTenantContext(counter: counter);
        await using var transaction = inTransaction ? await db.Database.BeginTransactionAsync() : null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var factory = new Factory(h, db, timeout.Token);

        object? instance;
        object?[] args;
        try
        {
            instance = factory.Construct(type, 0) ?? throw new InvalidOperationException("no usable constructor");
            args = method.GetParameters().Select(p => factory.Argument(p)).ToArray();
        }
        catch (Exception ex)
        {
            return (name, "unreached", "setup: " + Short(ex));
        }

        try
        {
            var result = method.Invoke(instance, args);
            if (result is Task task)
                await task;
            else if (result is not null && result.GetType().Name.StartsWith("ValueTask", StringComparison.Ordinal))
                await (Task)result.GetType().GetMethod("AsTask")!.Invoke(result, null)!;
            return counter.Count > 0 ? (name, "ran", "") : (name, "unreached", "completed without a command");
        }
        catch (Exception raw)
        {
            var ex = raw is TargetInvocationException { InnerException: { } inner } ? inner : raw;
            if (IsQueryFailure(ex))
                return (name, "failed", Short(ex));
            return counter.Count > 0 ? (name, "ran", "") : (name, "unreached", Short(ex));
        }
    }

    private static bool IsQueryFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql)
                return sql.Errors.Cast<SqlError>().Any(e => !ExpectedSqlErrors.Contains(e.Number));
            if (current is InvalidOperationException or NotSupportedException or ArgumentException
                && (current.StackTrace ?? "").Contains("Microsoft.EntityFrameworkCore.Query", StringComparison.Ordinal))
                return true;
            if (current.Message.Contains("could not be translated", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Short(Exception ex)
    {
        var e = ex;
        while (e.InnerException is not null && e is DbUpdateException or TargetInvocationException)
            e = e.InnerException;
        var message = e.Message.ReplaceLineEndings(" ");
        return $"{e.GetType().Name}: {(message.Length > 300 ? message[..300] : message)}";
    }

    private static IEnumerable<Type> QueryTypes() =>
        typeof(ApplicationDbContext).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && !t.IsNested)
            .Where(t => !typeof(IHostedService).IsAssignableFrom(t))
            .Where(t => t.Namespace is { } ns && !ns.Contains(".Seeders") && !ns.Contains(".Migrations"))
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(ApplicationDbContext))))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static IEnumerable<MethodInfo> QueryMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsGenericMethodDefinition && !m.IsSpecialName)
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType)
                || m.ReturnType.Name.StartsWith("ValueTask", StringComparison.Ordinal))
            .Where(m => m.GetParameters().All(p => !typeof(Delegate).IsAssignableFrom(p.ParameterType)
                && !typeof(System.Linq.Expressions.Expression).IsAssignableFrom(p.ParameterType)))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.GetParameters().Length);

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Interlocked.Increment(ref Count); return ValueTask.FromResult(result); }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        { Interlocked.Increment(ref Count); return ValueTask.FromResult(result); }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken ct = default)
        { Interlocked.Increment(ref Count); return ValueTask.FromResult(result); }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Interlocked.Increment(ref Count); return result; }
    }

    /// <summary>Builds constructor dependencies and method arguments.</summary>
    private sealed class Factory(MyTeamDb h, ApplicationDbContext db, CancellationToken ct)
    {
        private readonly SystemDateTimeProvider _clock = new();

        public object? Construct(Type type, int depth)
        {
            if (depth > 3)
                return null;
            foreach (var ctor in type.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
            {
                try
                {
                    return ctor.Invoke(ctor.GetParameters().Select(p => Dependency(p.ParameterType, depth)).ToArray());
                }
                catch
                {
                    // try the next constructor
                }
            }

            return null;
        }

        private object? Dependency(Type t, int depth)
        {
            if (t == typeof(ApplicationDbContext) || t == typeof(DbContext))
                return db;
            if (t == typeof(IDateTimeProvider))
                return _clock;
            if (t == typeof(TimeProvider))
                return TimeProvider.System;
            if (t == typeof(ITenantContext) || t == typeof(IWritableTenantContext))
                return TenantFrom(db);
            if (t == typeof(HttpClient))
                return new HttpClient(new NoNetwork());
            if (t == typeof(IConfiguration))
                return new ConfigurationBuilder().Build();
            if (t == typeof(ILoggerFactory))
                return NullLoggerFactory.Instance;
            if (t == typeof(ILogger))
                return NullLogger.Instance;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ILogger<>))
                return Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(t.GetGenericArguments()));
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IOptions<>))
            {
                var value = Activator.CreateInstance(t.GetGenericArguments()[0]);
                return typeof(Options).GetMethod(nameof(Options.Create))!.MakeGenericMethod(t.GetGenericArguments()[0]).Invoke(null, [value]);
            }
            if (t.IsInterface && t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t))
                return Array.CreateInstance(t.GetGenericArguments()[0], 0);
            if (t.IsInterface)
                return Fake.Create(t);
            if (t.IsValueType)
                return Activator.CreateInstance(t);
            if (t == typeof(string))
                return "x";
            if (t.IsClass && !t.IsAbstract)
                return Construct(t, depth + 1);
            return null;
        }

        private static ITenantContext? TenantFrom(ApplicationDbContext context) =>
            typeof(ApplicationDbContext).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => f.GetValue(context)).OfType<ITenantContext>().FirstOrDefault();

        public object? Argument(ParameterInfo p) => Value(p.ParameterType, p.Name ?? "", 0);

        private object? Value(Type t, string name, int depth)
        {
            var underlying = Nullable.GetUnderlyingType(t);
            if (underlying is not null)
                return Value(underlying, name, depth);
            if (t == typeof(CancellationToken))
                return ct;
            if (t == typeof(Guid))
                return name.Equals("tenantId", StringComparison.OrdinalIgnoreCase) ? h.TenantId
                    : name.Equals("legalEntityId", StringComparison.OrdinalIgnoreCase) ? h.LegalEntityId
                    : Guid.NewGuid();
            if (t == typeof(string))
                return "x";
            if (t == typeof(bool))
                return false;
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
                || t == typeof(decimal) || t == typeof(double) || t == typeof(float))
                return Convert.ChangeType(1, t);
            if (t == typeof(DateTime))
                return DateTime.UtcNow;
            if (t == typeof(DateTimeOffset))
                return DateTimeOffset.UtcNow;
            if (t == typeof(DateOnly))
                return DateOnly.FromDateTime(DateTime.UtcNow);
            if (t == typeof(TimeOnly))
                return new TimeOnly(9, 0);
            if (t == typeof(TimeSpan))
                return TimeSpan.FromHours(1);
            if (t.IsEnum)
                return Enum.GetValues(t).GetValue(0);
            if (t.IsArray)
                return OneItem(t.GetElementType()!, name, depth);
            if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t) && t.GetGenericArguments().Length == 1)
            {
                var list = OneItem(t.GetGenericArguments()[0], name, depth);
                if (t.IsInterface)
                    return list;
                var set = Activator.CreateInstance(t);
                t.GetMethod("Add")?.Invoke(set, [((Array)list).GetValue(0)]);
                return set;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                || t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                || t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(t.GetGenericArguments()));
            if (t.IsInterface)
                return Fake.Create(t);
            if (depth > 3)
                return null;
            return Build(t, depth + 1);
        }

        private Array OneItem(Type element, string name, int depth)
        {
            var array = Array.CreateInstance(element, 1);
            array.SetValue(Value(element, name.TrimEnd('s'), depth + 1), 0);
            return array;
        }

        private object? Build(Type t, int depth)
        {
            var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor is null)
                return null;
            var instance = ctor.Invoke(ctor.GetParameters().Select(p => Value(p.ParameterType, p.Name ?? "", depth)).ToArray());
            foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(pr => pr.CanWrite && pr.GetIndexParameters().Length == 0))
            {
                var current = property.GetValue(instance);
                var type = property.PropertyType;
                var isEmpty = current is null || (type == typeof(Guid) && (Guid)current == Guid.Empty);
                if (!isEmpty || (Nullable.GetUnderlyingType(type) is not null && property.Name != "Id"))
                    continue;
                if (type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type))
                    continue; // navigation properties stay null
                try
                {
                    property.SetValue(instance, Value(type, property.Name, depth));
                }
                catch
                {
                    // init-only or validated setters keep their default
                }
            }

            return instance;
        }
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("The query sweep makes no network calls.");
    }

    /// <summary>An interface implementation whose members return empty defaults.</summary>
    public class Fake : DispatchProxy
    {
        public static object Create(Type t) =>
            typeof(DispatchProxy).GetMethod(nameof(DispatchProxy.Create), 2, Type.EmptyTypes)!
                .MakeGenericMethod(t, typeof(Fake)).Invoke(null, null)!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var r = targetMethod!.ReturnType;
            if (r == typeof(void))
                return null;
            if (r == typeof(Task))
                return Task.CompletedTask;
            if (r == typeof(ValueTask))
                return ValueTask.CompletedTask;
            if (r.IsGenericType && r.GetGenericTypeDefinition() == typeof(Task<>))
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(r.GetGenericArguments()[0])
                    .Invoke(null, [DefaultOf(r.GetGenericArguments()[0])]);
            if (r.IsGenericType && r.GetGenericTypeDefinition() == typeof(ValueTask<>))
                return Activator.CreateInstance(r, DefaultOf(r.GetGenericArguments()[0]));
            return DefaultOf(r);
        }

        private static object? DefaultOf(Type t)
        {
            if (t.IsValueType)
                return Activator.CreateInstance(t);
            if (t == typeof(string))
                return "";
            if (t.IsArray)
                return Array.CreateInstance(t.GetElementType()!, 0);
            if (t.IsInterface && t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t))
            {
                var args = t.GetGenericArguments();
                return args.Length == 2
                    ? Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args))
                    : Array.CreateInstance(args[0], 0);
            }

            return null;
        }
    }
}
