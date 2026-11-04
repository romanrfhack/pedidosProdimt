using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class ProductionRetryContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpConflictPreservesErrorAndAddsExplicitRetryDisposition(bool retrySameOperation)
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IOrderOperationStore>(_ => new ConflictStore(retrySameOperation))));
        using var client = factory.CreateClient();
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/admin/login",
            new AdminLoginRequest(DevelopmentAuthSeedValues.DefaultAdminUserName, DevelopmentAuthSeedValues.DefaultAdminPassword));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        using var response = await client.PostAsJsonAsync("/api/admin/operations/production/batches",
            new RegisterProductionRequest(Guid.NewGuid(), new DateOnly(2026, 11, 2), Guid.NewGuid(), Guid.NewGuid(), 7));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Synthetic conflict", body.GetProperty("error").GetString());
        Assert.Equal(retrySameOperation, body.GetProperty("retrySameOperation").GetBoolean());
    }

    [Theory]
    [InlineData(1205, false)]
    [InlineData(1205, true)]
    [InlineData(2601, false)]
    [InlineData(2601, true)]
    [InlineData(2627, false)]
    [InlineData(2627, true)]
    public async Task ConcurrentProviderFailuresRequireTheSameKeyAndRollbackWithoutReceipt(int number, bool wrapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        Exception failure = CreateSqlException(number);
        if (wrapped) failure = new InvalidOperationException("Synthetic wrapper", new DbUpdateException("Synthetic provider", failure));
        var actual = await Assert.ThrowsAsync<CustomerOrderConflictException>(() => new EfOrderOperationStore(db).ExecuteAsync(
            "PRODUCTION:BATCH:test", "hash", DateTimeOffset.UtcNow, _ => Task.FromException<int>(failure), CancellationToken.None));
        Assert.True(actual.RetrySameOperation);
        Assert.Empty(await db.OrderOperations.ToArrayAsync());
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task EfConcurrencyFailureKeepsTheSameOperationIdentity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var actual = await Assert.ThrowsAsync<CustomerOrderConflictException>(() => new EfOrderOperationStore(db).ExecuteAsync(
            "PRODUCTION:BATCH:concurrent", "hash", DateTimeOffset.UtcNow,
            _ => Task.FromException<int>(new DbUpdateConcurrencyException("Synthetic conflict")), CancellationToken.None));
        Assert.True(actual.RetrySameOperation); Assert.Empty(await db.OrderOperations.ToArrayAsync());
    }

    [Fact]
    public async Task HashMismatchKeepsExistingReceiptAndOriginalPayloadStillReplays()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfOrderOperationStore(db); var writes = 0;
        Task<int> Apply(CancellationToken _) => Task.FromResult(++writes);
        await store.ExecuteAsync("PRODUCTION:BATCH:receipt", "original", DateTimeOffset.UtcNow, Apply, CancellationToken.None);
        var collision = await Assert.ThrowsAsync<CustomerOrderConflictException>(() => store.ExecuteAsync(
            "PRODUCTION:BATCH:receipt", "different", DateTimeOffset.UtcNow, Apply, CancellationToken.None));
        Assert.True(collision.RetrySameOperation);
        var replay = await store.ExecuteAsync("PRODUCTION:BATCH:receipt", "original", DateTimeOffset.UtcNow, Apply, CancellationToken.None);
        Assert.True(replay.Replayed); Assert.Equal(1, replay.Result); Assert.Equal(1, writes); Assert.Single(await db.OrderOperations.ToArrayAsync());
    }

    [Fact]
    public async Task TransactionalBusinessRejectionIsExplicitlyDefinitiveAndHasNoReceipt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var actual = await Assert.ThrowsAsync<CustomerOrderConflictException>(() => new EfOrderOperationStore(db).ExecuteAsync(
            "PRODUCTION:ALLOCATE:business", "hash", DateTimeOffset.UtcNow,
            _ => Task.FromException<int>(new CustomerOrderConflictException("Insufficient synthetic stock")), CancellationToken.None));
        Assert.False(actual.RetrySameOperation); Assert.Empty(await db.OrderOperations.ToArrayAsync()); Assert.Null(db.Database.CurrentTransaction);
    }

    private sealed class ConflictStore(bool retrySameOperation) : IOrderOperationStore
    {
        public Task<OrderOperationResult<T>> ExecuteAsync<T>(string operationKey, string payloadHash, DateTimeOffset occurredAt,
            Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
            => throw new CustomerOrderConflictException("Synthetic conflict", retrySameOperation);
    }

    private static SqlException CreateSqlException(int number)
    {
        // SqlClient has no public factory. The real EF store runs with SQLite; only this provider failure is synthetic.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        Type[] prefix = [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int)];
        var constructor = typeof(SqlError).GetConstructors(instance)
            .First(x => x.GetParameters().Select(p => p.ParameterType).Take(prefix.Length).SequenceEqual(prefix));
        object?[] arguments = [number, (byte)0, (byte)16, "synthetic-server", "Synthetic SQL failure", "synthetic-procedure", 1,
            .. constructor.GetParameters().Skip(prefix.Length).Select(p => p.HasDefaultValue ? p.DefaultValue :
                p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)];
        var error = (SqlError)constructor.Invoke(arguments);
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", instance, null, [typeof(SqlError)], null)!.Invoke(errors, [error]);
        var factory = typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic,
            null, [typeof(SqlErrorCollection), typeof(string)], null)!;
        return (SqlException)factory.Invoke(null, [errors, "16.0.0"])!;
    }
}
