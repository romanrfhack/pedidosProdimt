using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class CustomerOrderPersistenceTests
{
    [Fact]
    public async Task GetTodayAsync_IncludesCurrentOrderWhenCustomerAlreadyResponded()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 30, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var today = await fixture.CustomerOrders.GetTodayAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);

        Assert.NotNull(today.CurrentOrder);
        Assert.Equal(response.OrderId, today.CurrentOrder.OrderId);
        Assert.Equal(OrderStatus.Submitted, today.CurrentOrder.Status);
        Assert.Equal(1, today.CurrentOrder.SequenceNumber);
        Assert.False(today.CurrentOrder.RequiresAdminReview);
    }

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

    [Fact]
    public async Task SubmitAsync_RejectsOrderWithoutPositiveQuantities()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 0),
            CancellationToken.None));

        Assert.Contains("Captura al menos una cantidad", exception.Message);
        Assert.False(await fixture.DbContext.Orders.AnyAsync());
    }

    [Fact]
    public async Task SubmitAsync_RejectsNegativeQuantities()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, -1),
            CancellationToken.None));

        Assert.Contains("cantidades no pueden ser negativas", exception.Message);
        Assert.False(await fixture.DbContext.Orders.AnyAsync());
    }

    [Fact]
    public async Task SubmitAsync_IgnoresZeroQuantityLines()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new SubmitCustomerOrderRequest(
            [
                new SubmitCustomerOrderLineRequest(DevelopmentSeedIds.ProductNineAndHalfId, 0, Notes: null),
                new SubmitCustomerOrderLineRequest(DevelopmentSeedIds.ProductTenAndHalfId, 10, Notes: null)
            ]),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == response.OrderId);

        var line = Assert.Single(persisted.Lines);
        Assert.Equal(DevelopmentSeedIds.ProductTenAndHalfId, line.ProductId);
        Assert.Equal(10, line.Quantity);
    }

    [Fact]
    public async Task MarkNoOrderAsync_ReturnsExistingNoOrderWithoutDuplicate()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var first = await fixture.CustomerOrders.MarkNoOrderAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);
        var second = await fixture.CustomerOrders.MarkNoOrderAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);

        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(1, await fixture.DbContext.Orders.CountAsync(x => x.CustomerId == DevelopmentSeedIds.GranTakitoCustomerId));
    }

    [Fact]
    public async Task AdminSummary_IncludesCustomerNameAndAdminDecision()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 10, 1, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var summaries = await fixture.AdminOrders.GetPendingReviewAsync(CancellationToken.None);
        var summary = Assert.Single(summaries);

        Assert.Equal(response.OrderId, summary.OrderId);
        Assert.Equal("Gran Takito", summary.CustomerName);
        Assert.Equal(AdminDecision.Pending, summary.AdminDecision);
    }

    [Theory]
    [InlineData(AdminDecision.Accepted, OrderStatus.Accepted)]
    [InlineData(AdminDecision.Rejected, OrderStatus.Rejected)]
    [InlineData(AdminDecision.AcceptedWithChanges, OrderStatus.Accepted)]
    public async Task ReviewAsync_PersistsAdminDecision(
        AdminDecision decision,
        OrderStatus expectedStatus)
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 10, 1, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var reviewed = await fixture.AdminOrders.ReviewAsync(
            response.OrderId,
            new ReviewOrderRequest(decision, InternalNotes: "Decision revisada por admin."),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders.SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(decision, reviewed.AdminDecision);
        Assert.Equal(decision, persisted.AdminDecision);
        Assert.Equal(expectedStatus, persisted.Status);
        Assert.False(persisted.RequiresAdminReview);
        Assert.Equal("Decision revisada por admin.", persisted.InternalNotes);
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
            var customerRepository = new EfCustomerRepository(dbContext);
            var orderRepository = new EfOrderRepository(dbContext);

            CustomerOrders = new CustomerOrderService(
                customerRepository,
                new EfProductRepository(dbContext),
                new EfSalesChannelRepository(dbContext),
                orderRepository,
                dateTimeProvider);
            AdminOrders = new AdminOrderService(orderRepository, customerRepository, dateTimeProvider);
        }

        public PedidosDbContext DbContext { get; }

        public MutableDateTimeProvider DateTimeProvider { get; }

        public CustomerOrderService CustomerOrders { get; }

        public AdminOrderService AdminOrders { get; }

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
