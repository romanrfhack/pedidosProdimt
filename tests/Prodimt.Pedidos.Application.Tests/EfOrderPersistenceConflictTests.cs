using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class EfOrderPersistenceConflictTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SaveChanges_DeadlockBecomesOrderConflict_ThroughProviderWrappers(int wrapperDepth)
    {
        var failure = Wrap(CreateSqlException(1205), wrapperDepth);
        await using var db = CreateContext(failure);

        await Assert.ThrowsAsync<CustomerOrderConflictException>(() =>
            new EfOrderRepository(db).SaveChangesAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SaveChanges_ConstraintFailureRetainsOriginalException_ThroughProviderWrappers(int wrapperDepth)
    {
        var failure = Wrap(CreateSqlException(547), wrapperDepth);
        await using var db = CreateContext(failure);

        var actual = await Record.ExceptionAsync(() =>
            new EfOrderRepository(db).SaveChangesAsync(CancellationToken.None));

        Assert.Same(failure, actual);
    }

    private static PedidosDbContext CreateContext(Exception failure) => new(
        new DbContextOptionsBuilder<PedidosDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(new ProviderFailure(failure))
            .Options);

    private static Exception Wrap(SqlException failure, int depth) => depth switch
    {
        0 => failure,
        1 => new DbUpdateException("Synthetic provider update failure", failure),
        2 => new InvalidOperationException("Synthetic execution strategy wrapper",
            new DbUpdateException("Synthetic provider update failure", failure)),
        _ => throw new ArgumentOutOfRangeException(nameof(depth))
    };

    private static SqlException CreateSqlException(int number)
    {
        // SqlClient exposes no public factory. Reflection stays in tests; the interceptor
        // exercises the real repository exception boundary without requiring a SQL server.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        Type[] prefix = [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int)];
        var errorConstructor = typeof(SqlError).GetConstructors(instance)
            .First(x => x.GetParameters().Select(p => p.ParameterType).Take(prefix.Length).SequenceEqual(prefix));
        var parameters = errorConstructor.GetParameters();
        object?[] arguments = [number, (byte)0, (byte)16, "synthetic-server", "Synthetic SQL failure", "synthetic-procedure", 1,
            .. parameters.Skip(prefix.Length).Select(p => p.HasDefaultValue ? p.DefaultValue :
                p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)];
        var error = (SqlError)errorConstructor.Invoke(arguments);
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", instance, null, [typeof(SqlError)], null)!
            .Invoke(errors, [error]);
        var factory = typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic,
            null, [typeof(SqlErrorCollection), typeof(string)], null)!;
        var exception = (SqlException)factory.Invoke(null, [errors, "16.0.0"])!;
        Assert.Equal(number, exception.Number);
        return exception;
    }

    private sealed class ProviderFailure(Exception failure) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => ValueTask.FromException<InterceptionResult<int>>(failure);
    }
}
