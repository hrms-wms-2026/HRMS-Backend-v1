using FluentAssertions;
using System.Collections;
using System.Data;
using System.Data.Common;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

public class TenantRlsInterceptorTests
{
    [Fact]
    public void TenantMode_ConnectionOpened_SetsTenantIdAndMode()
    {
        var tenantId = Guid.NewGuid();
        var ctx = new Mock<ITenantContext>();
        ctx.Setup(c => c.ContextMode).Returns(TenantContextMode.Tenant);
        ctx.Setup(c => c.TenantId).Returns(tenantId);
        var connection = new RecordingDbConnection();

        var interceptor = new TenantRlsInterceptor(ctx.Object);
        interceptor.ConnectionOpened(connection, null!);

        connection.LastCommand!.CommandText.Should().Contain("app.current_tenant_id");
        connection.LastCommand!.CommandText.Should().Contain("app.tenant_context_mode");
        connection.LastCommand!.ParametersByName["tenant_id"].Should().Be(tenantId.ToString());
        connection.LastCommand!.ParametersByName["mode"].Should().Be("tenant");
        connection.LastCommand!.ExecuteCount.Should().Be(1);
    }

    [Fact]
    public void AdminMode_ConnectionOpened_SetsAdminModeWithoutTenantId()
    {
        var ctx = new Mock<ITenantContext>();
        ctx.Setup(c => c.ContextMode).Returns(TenantContextMode.Admin);
        ctx.Setup(c => c.TenantId).Returns(Guid.Empty);
        var connection = new RecordingDbConnection();

        var interceptor = new TenantRlsInterceptor(ctx.Object);
        interceptor.ConnectionOpened(connection, null!);

        connection.LastCommand!.ParametersByName["tenant_id"].Should().Be(string.Empty);
        connection.LastCommand!.ParametersByName["mode"].Should().Be("admin");
        connection.LastCommand!.ExecuteCount.Should().Be(1);
    }

    [Fact]
    public void ConnectionClosing_ResetsTenantIdAndMode()
    {
        var ctx = new Mock<ITenantContext>();
        ctx.Setup(c => c.ContextMode).Returns(TenantContextMode.Tenant);
        ctx.Setup(c => c.TenantId).Returns(Guid.NewGuid());
        var connection = new RecordingDbConnection();

        var interceptor = new TenantRlsInterceptor(ctx.Object);
        interceptor.ConnectionClosing(connection, null!, default);

        connection.LastCommand!.ParametersByName["tenant_id"].Should().Be(string.Empty);
        connection.LastCommand!.ParametersByName["mode"].Should().Be(string.Empty);
        connection.LastCommand!.ExecuteCount.Should().Be(1);
    }

    [Fact]
    public void TwoScopedContexts_EachFiltersByOwnTenantId()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var ctxA = new Mock<ITenantContext>();
        ctxA.Setup(c => c.TenantId).Returns(tenantA);
        ctxA.Setup(c => c.ContextMode).Returns(TenantContextMode.Tenant);

        var ctxB = new Mock<ITenantContext>();
        ctxB.Setup(c => c.TenantId).Returns(tenantB);
        ctxB.Setup(c => c.ContextMode).Returns(TenantContextMode.Tenant);

        ctxA.Object.TenantId.Should().Be(tenantA);
        ctxB.Object.TenantId.Should().Be(tenantB);
        ctxA.Object.TenantId.Should().NotBe(ctxB.Object.TenantId);
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        public RecordingDbCommand? LastCommand { get; private set; }
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand()
        {
            LastCommand = new RecordingDbCommand(this);
            return LastCommand;
        }
    }

    private sealed class RecordingDbCommand(DbConnection connection) : DbCommand
    {
        private readonly RecordingParameterCollection _parameters = new();
        public int ExecuteCount { get; private set; }
        public IReadOnlyDictionary<string, object?> ParametersByName => _parameters.ByName;
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection DbConnection { get; set; } = connection;
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery()
        {
            ExecuteCount++;
            return 1;
        }
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new RecordingDbParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class RecordingDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class RecordingParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
        public IReadOnlyDictionary<string, object?> ByName =>
            _items.ToDictionary(p => p.ParameterName, p => p.Value);

        public override int Count => _items.Count;
        public override object SyncRoot => this;
        public override int Add(object value)
        {
            _items.Add((DbParameter)value);
            return _items.Count - 1;
        }
        public override void AddRange(Array values)
        {
            foreach (var value in values) Add(value!);
        }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => _items.Any(p => p.ParameterName == value);
        public override void CopyTo(Array array, int index) => _items.ToArray().CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index < 0) _items.Add(value);
            else _items[index] = value;
        }
    }
}
