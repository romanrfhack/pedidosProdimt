using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class CustomerOrderPersistenceTests
{
    [Fact]
    public async Task SubmitAsync_PersistsNormalOrder()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 30, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(OrderStatus.Submitted, response.Status);
        Assert.False(response.IsLate);
        Assert.False(response.RequiresAdminReview);
        Assert.Null(response.AdminReviewReason);
        Assert.Single(persisted.Lines);
        Assert.Equal(20, persisted.Lines.Single().Quantity);
    }

    [Fact]
    public async Task SubmitAsync_PersistsLateOrderAsPendingReview()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 10, 1, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders.SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(OrderStatus.PendingAdminReview, persisted.Status);
        Assert.True(persisted.IsLate);
        Assert.True(persisted.RequiresAdminReview);
        Assert.Equal(AdminReviewReason.LateSubmission, persisted.AdminReviewReason);
    }

    [Fact]
    public async Task MarkNoOrderAsync_PersistsNoOrderResponse()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.MarkNoOrderAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(OrderStatus.NoOrder, persisted.Status);
        Assert.Equal(DevelopmentSeedIds.GranTakitoCustomerId, persisted.CustomerId);
        Assert.Empty(persisted.Lines);
        Assert.False(persisted.RequiresAdminReview);
    }

    [Fact]
    public async Task SubmitAsync_SecondOrderSameDayRequiresAdminReview()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        fixture.DateTimeProvider.Now = new DateTimeOffset(2026, 5, 27, 9, 30, 0, TimeSpan.Zero);

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductTenAndHalfId, 10),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders.SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(OrderStatus.PendingAdminReview, persisted.Status);
        Assert.False(persisted.IsLate);
        Assert.True(persisted.RequiresAdminReview);
        Assert.Equal(AdminReviewReason.AdditionalOrderSameDay, persisted.AdminReviewReason);
        Assert.Equal(2, persisted.SequenceNumber);
    }

    private static SubmitCustomerOrderRequest CreateSubmitRequest(Guid productId, decimal quantity)
    {
        return new SubmitCustomerOrderRequest(
        [
            new SubmitCustomerOrderLineRequest(productId, quantity, Notes: null)
        ]);
    }

    private sealed class MutableDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SqliteFixture(SqliteConnection connection, PedidosDbContext dbContext, MutableDateTimeProvider dateTimeProvider)
        {
            _connection = connection;
            DbContext = dbContext;
            DateTimeProvider = dateTimeProvider;
            CustomerOrders = new CustomerOrderService(
                new EfCustomerRepository(dbContext),
                new EfProductRepository(dbContext),
                new EfSalesChannelRepository(dbContext),
                new EfOrderRepository(dbContext),
                dateTimeProvider);
        }

        public PedidosDbContext DbContext { get; }

        public MutableDateTimeProvider DateTimeProvider { get; }

        public CustomerOrderService CustomerOrders { get; }

        public static async Task<SqliteFixture> CreateAsync(DateTimeOffset now)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<PedidosDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new PedidosDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            await PedidosDevelopmentSeeder.SeedAsync(dbContext);

            return new SqliteFixture(connection, dbContext, new MutableDateTimeProvider(now));
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
