using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Collections;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class CollectionServiceTests
{
    private static readonly CancellationToken Ct = default;

    [Theory]
    [InlineData("credit")]
    [InlineData(null)]
    public async Task NonCashDeliveryDoesNotCreateFinancialMovementsAndCannotCollect(string? paymentTerm)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Assignment.PaymentTermCode = paymentTerm;
        fixture.Order.PaymentTermCode = paymentTerm;
        await fixture.Db.SaveChangesAsync(Ct);
        var preview = await fixture.Preview(fixture.Assignment, 5);
        Assert.Null(preview.SuggestedAmount);
        Assert.Empty(preview.TicketLines);
        await fixture.Capture(fixture.Assignment, 5, null);
        Assert.Empty(await fixture.Db.CollectionCharges.ToArrayAsync(Ct));
        Assert.Empty(await fixture.Db.CollectionPayments.ToArrayAsync(Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Capture(fixture.Assignment, 1, 1));
        Assert.Empty(await fixture.Db.CollectionCharges.ToArrayAsync(Ct));
        Assert.Empty(await fixture.Db.CollectionPayments.ToArrayAsync(Ct));
    }

    [Fact]
    public async Task ANewCreditOrderDoesNotJoinAnAlreadyTrackedCashDay()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Capture(fixture.Assignment, 1, null);
        var originalLine = fixture.Order.Lines.Single();
        var credit = Order.CreateSubmitted(fixture.Customer.Id, fixture.Order.SalesChannelId, fixture.Clock.Today,
            fixture.Clock.Now, 2, new(false, false, null, OrderStatus.Submitted),
            [new OrderLine { Id = Guid.NewGuid(), ProductId = originalLine.ProductId,
                ProductNameSnapshot = originalLine.ProductNameSnapshot, UnitCode = "kg",
                Quantity = 4, UnitPriceSnapshot = 50, CurrencyCodeSnapshot = "MXN" }], fixture.Customer);
        credit.PaymentTermCode = "credit";
        fixture.Db.Add(credit);
        await fixture.Db.SaveChangesAsync(Ct);
        await fixture.Operations.ExecuteAsync("CREDIT-ORDER", "CREDIT-ORDER", fixture.Clock.Now, async ct =>
        {
            await fixture.Service.ReconcileOrderAsync(credit, fixture.Cashier, ct);
            return true;
        }, Ct);
        var preview = await fixture.Preview(fixture.Assignment, 1);
        Assert.Equal(500, preview.TicketTotal);
        Assert.Single(await fixture.Db.CollectionCharges.ToArrayAsync(Ct));
    }

    [Theory]
    [InlineData(400, 400, 400, 0, 0)]
    [InlineData(600, 200, 500, 100, 0)]
    [InlineData(800, 0, 500, 300, 0)]
    [InlineData(900, 0, 500, 300, 100)]
    public async Task PartialDeliveryChargesTheWholeDayAndPaymentAppliesTodayThenOldDebt(
        decimal payment, decimal debt, decimal appliedToday, decimal appliedYesterday, decimal credit)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Capture(fixture.PreviousAssignment, 1, null);
        var preview = await fixture.Preview(fixture.Assignment, 5);
        Assert.Equal(500, preview.TicketTotal);
        Assert.Equal(300, preview.PriorBalance);
        Assert.Equal(800, preview.SuggestedAmount);
        await fixture.Capture(fixture.Assignment, 5, payment);

        var account = await fixture.Service.CustomerAccountAsync(fixture.Customer.Id, Ct);
        Assert.Equal(800, account.ChargesTotal);
        Assert.Equal(payment, account.PaidTotal);
        Assert.Equal(debt, account.Balance);
        Assert.Equal(credit, account.CreditBalance);
        Assert.Equal(10, Assert.Single(account.Charges, x => x.Date == fixture.Clock.Today).RequestedQuantity);
        var saved = await fixture.Db.CollectionPayments.SingleAsync(Ct);
        Assert.Equal(credit, saved.CreditAmount);
        using var applications = JsonDocument.Parse(saved.AllocationsJson);
        var rows = applications.RootElement.EnumerateArray().ToArray();
        Assert.Equal(appliedToday, rows.Single(x => x.GetProperty("Date").GetString() == fixture.Clock.Today.ToString("yyyy-MM-dd")).GetProperty("Amount").GetDecimal());
        Assert.Equal(appliedYesterday, rows.Where(x => x.GetProperty("Date").GetString() != fixture.Clock.Today.ToString("yyyy-MM-dd"))
            .Sum(x => x.GetProperty("Amount").GetDecimal()));
    }

    [Fact]
    public async Task CashDifferenceDoesNotUndoCustomerPaymentAndReceiptReplayDoesNotDuplicateCash()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Capture(fixture.Assignment, 5, 80);
        var dashboard = await fixture.Service.CashierDashboardAsync(null, null, fixture.Cashier, Ct);
        var row = Assert.Single(dashboard.Trips);
        var request = new ReceiveCollectionCashRequest(Guid.NewGuid(), row.TripId, row.Version, 80, 75, "Diferencia en conteo");
        var receipt = await fixture.Service.ReceiveCashAsync(request, fixture.Cashier, Ct);
        var replay = await fixture.Service.ReceiveCashAsync(request, fixture.Cashier, Ct);
        Assert.Equal(receipt.Id, replay.Id);
        Assert.True(replay.Replayed);
        row = Assert.Single((await fixture.Service.CashierDashboardAsync(null, null, fixture.Cashier, Ct)).Trips);
        Assert.Equal(0, row.PendingAmount);
        Assert.Equal(5, row.Difference);
        Assert.Equal(75, row.ReceivedAmount);
        Assert.Equal(420, (await fixture.Service.CustomerAccountAsync(fixture.Customer.Id, Ct)).Balance);
        Assert.Equal(fixture.Cashier.ActorId, (await fixture.Db.CollectionCashReceipts.SingleAsync(Ct)).ReceiverUserId.ToString());
    }

    [Fact]
    public async Task PendingPricePreservesRealPaymentAndLaterValuationIsAppendOnly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var line = fixture.Order.Lines.Single();
        line.UnitPriceSnapshot = null;
        line.CurrencyCodeSnapshot = null;
        await fixture.Db.SaveChangesAsync(Ct);
        var preview = await fixture.Preview(fixture.Assignment, 1);
        Assert.Null(preview.TicketTotal);
        Assert.True(preview.PricePending);
        await fixture.Capture(fixture.Assignment, 1, 400);
        Assert.Null((await fixture.Db.CollectionCharges.SingleAsync(Ct)).Amount);
        Assert.Null((await fixture.Db.CollectionPayments.SingleAsync(Ct)).CreditAmount);
        Assert.Null((await fixture.Service.CustomerAccountAsync(fixture.Customer.Id, Ct)).Balance);

        line.UnitPriceSnapshot = 50;
        line.CurrencyCodeSnapshot = "MXN";
        await fixture.Operations.ExecuteAsync("RESOLVE-PRICE", "RESOLVE-PRICE", fixture.Clock.Now, async ct =>
        {
            await fixture.Service.ReconcilePendingPricesAsync(fixture.Order, line, fixture.Cashier, ct);
            return true;
        }, Ct);
        var account = await fixture.Service.CustomerAccountAsync(fixture.Customer.Id, Ct);
        Assert.Equal(100, account.Balance);
        Assert.Equal(500, account.ChargesTotal);
        Assert.Null((await fixture.Db.CollectionCharges.SingleAsync(Ct)).Amount);
        Assert.Single(await fixture.Db.CollectionChargeValuations.ToArrayAsync(Ct));
    }

    [Fact]
    public async Task OrderCorrectionsAdjustDebtWhileDeliveryCorrectionsLeaveItUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Capture(fixture.Assignment, 5, 400);
        await fixture.Operations.ExecuteAsync("CORRECT-ORDER", "CORRECT-ORDER", fixture.Clock.Now, async ct =>
        {
            fixture.Order.Lines.Single().Quantity = 12;
            fixture.Order.Version++;
            await fixture.Service.ReconcileOrderAsync(fixture.Order, fixture.Cashier, ct);
            // Repeated hooks in one transaction see their own pending ledger rows.
            await fixture.Service.ReconcileOrderAsync(fixture.Order, fixture.Cashier, ct);
            return true;
        }, Ct);
        var account = await fixture.Service.CustomerAccountAsync(fixture.Customer.Id, Ct);
        Assert.Equal(600, account.ChargesTotal);
        Assert.Equal(200, account.Balance);
        Assert.Equal(2, account.Charges.Count);
        await fixture.Service.RecordDeliveryAdjustmentAsync(fixture.Assignment, fixture.Trip, fixture.Order,
            fixture.Order.Lines.Single(), Guid.NewGuid(), -2, "Corregir kilos", fixture.Cashier, Ct);
        Assert.Equal(600, (await fixture.Service.CustomerAccountAsync(fixture.Customer.Id, Ct)).ChargesTotal);
    }

    [Fact]
    public async Task ReceiptPermissionsAreCheckedBeforeReplayAndOtherDriversCannotReadTheAccount()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Capture(fixture.Assignment, 5, 80);
        var row = Assert.Single((await fixture.Service.CashierDashboardAsync(null, null, fixture.Cashier, Ct)).Trips);
        var request = new ReceiveCollectionCashRequest(Guid.NewGuid(), row.TripId, row.Version, null, 80, null);
        await fixture.Service.ReceiveCashAsync(request, fixture.Cashier, Ct);
        fixture.Security.Receive = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ReceiveCashAsync(request, fixture.Cashier, Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.PreviewAsync(fixture.Assignment.Id, 1,
            new(Guid.NewGuid().ToString(), "Otra persona"), false, Ct));
        Assert.Single(await fixture.Db.CollectionCashReceipts.ToArrayAsync(Ct));
    }

    [Fact]
    public async Task ReceiptUserDailyTotalUsesReceptionDateAcrossPreviousServiceDays()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Capture(fixture.PreviousAssignment, 1, 100);
        var row = Assert.Single((await fixture.Service.CashierDashboardAsync(fixture.Clock.Today.AddDays(-1), null, fixture.Cashier, Ct)).Trips);
        await fixture.Service.ReceiveCashAsync(new(Guid.NewGuid(), row.TripId, row.Version, null, 100, null), fixture.Cashier, Ct);
        var today = await fixture.Service.CashierDashboardAsync(fixture.Clock.Today, null, fixture.Cashier, Ct);
        Assert.Equal(100, today.MyReceivedAmount);
        Assert.DoesNotContain(today.Trips, trip => trip.Date != fixture.Clock.Today);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public PedidosDbContext Db { get; }
        public IOrderOperationStore Operations { get; }
        public CollectionService Service { get; }
        public Clock Clock { get; } = new();
        public Security Security { get; } = new();
        public Customer Customer { get; }
        public DeliveryTrip Trip { get; }
        public Order Order { get; }
        public DeliveryAssignment Assignment { get; }
        public DeliveryAssignment PreviousAssignment { get; }
        public AdminActorContext Driver { get; }
        public AdminActorContext Cashier { get; }

        private Fixture(SqliteConnection connection, PedidosDbContext db, Customer customer, DeliveryTrip trip,
            Order order, DeliveryAssignment assignment, DeliveryAssignment previousAssignment, AdminUser driver, AdminUser cashier)
        {
            this.connection = connection; Db = db; Customer = customer; Trip = trip; Order = order;
            Assignment = assignment; PreviousAssignment = previousAssignment;
            Driver = new(driver.Id.ToString(), driver.DisplayName);
            Cashier = new(cashier.Id.ToString(), cashier.DisplayName);
            Operations = new EfOrderOperationStore(db);
            Service = new(new EfCollectionRepository(db), new EfDeliveryRepository(db), new EfOrderRepository(db),
                new EfCustomerRepository(db), Operations, new EfAuditLogRepository(db), Clock, Security);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(Ct);
            var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync(Ct);
            var clock = new Clock();
            var customer = Customer.Create("Cliente financiero", null, null, null, null, null, clock.Now);
            customer.SetPilotDelivery("R1", 1, null, "cash_on_delivery", null, clock.Now);
            var product = Product.Create("Tortilla", null); product.SetUnitCode("kg");
            var driver = AdminUser.Create("chofer", "Chofer real", "test", clock.Now);
            var cashier = AdminUser.Create("caja", "Mostrador real", "test", clock.Now);
            var channel = new SalesChannel { Id = Guid.NewGuid(), Name = "Prueba", Type = SalesChannelType.AdminManualCapture };
            Order MakeOrder(DateOnly date, decimal quantity) => Order.CreateSubmitted(customer.Id, channel.Id, date,
                clock.Now, 1, new(false, false, null, OrderStatus.Submitted),
                [new OrderLine { Id = Guid.NewGuid(), ProductId = product.Id, ProductNameSnapshot = product.Name,
                    UnitCode = "kg", Quantity = quantity, UnitPriceSnapshot = 50, CurrencyCodeSnapshot = "MXN" }], customer);
            DeliveryTrip MakeTrip(DateOnly date) => new() { Id = Guid.NewGuid(), Date = date, RouteCode = "R1",
                RouteName = "Ruta uno", DriverUserId = driver.Id, DriverName = driver.DisplayName,
                CreatedAt = clock.Now, DepartedAt = clock.Now, TripNumber = 1 };
            DeliveryAssignment MakeAssignment(Order order, DeliveryTrip trip) => new() { Id = Guid.NewGuid(), TripId = trip.Id,
                OrderId = order.Id, OrderLineId = order.Lines.Single().Id, CustomerId = customer.Id, CustomerName = customer.Name,
                ProductId = product.Id, ProductName = product.Name, UnitCode = "kg", PaymentTermCode = "cash_on_delivery",
                RequestedQuantity = order.Lines.Single().Quantity, AssignedQuantity = order.Lines.Single().Quantity,
                LoadedQuantity = order.Lines.Single().Quantity, AcknowledgedOrderVersion = 1 };
            var order = MakeOrder(clock.Today, 10);
            var previous = MakeOrder(clock.Today.AddDays(-1), 6);
            var trip = MakeTrip(order.OrderDate);
            var previousTrip = MakeTrip(previous.OrderDate);
            var assignment = MakeAssignment(order, trip);
            var previousAssignment = MakeAssignment(previous, previousTrip);
            db.AddRange(customer, product, driver, cashier, channel,
                new DeliveryRoute { Code = "R1", Name = "Ruta uno" }, order, previous, trip, previousTrip, assignment, previousAssignment);
            await db.SaveChangesAsync(Ct);
            return new(connection, db, customer, trip, order, assignment, previousAssignment, driver, cashier);
        }

        public Task<CollectionPreviewResponse> Preview(DeliveryAssignment assignment, decimal quantity) =>
            Service.PreviewAsync(assignment.Id, quantity, Driver, false, Ct);

        public async Task Capture(DeliveryAssignment assignment, decimal quantity, decimal? payment)
        {
            var preview = await Preview(assignment, quantity);
            await Operations.ExecuteAsync("DELIVER:" + Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString(), Clock.Now, async ct =>
            {
                var trip = await new EfDeliveryRepository(Db).GetTripAsync(assignment.TripId, ct);
                var order = await new EfOrderRepository(Db).GetByIdAsync(assignment.OrderId, ct);
                var entry = new DeliveryEvent { Id = Guid.NewGuid(), TripId = assignment.TripId, AssignmentId = assignment.Id,
                    Type = "Delivered", Quantity = quantity, OccurredAt = Clock.Now };
                Db.Add(entry);
                assignment.DeliveredQuantity += quantity;
                await Service.RecordDeliveryAsync(assignment, trip!, order!, order!.Lines.Single(), entry.Id,
                    quantity, payment, preview.Version, Driver, false, ct);
                return true;
            }, Ct);
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset Now => new(2026, 11, 22, 16, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 11, 22);
        public TimeOnly LocalTimeOfDay => new(10, 0);
    }

    private sealed class Security : ISecurityStore
    {
        public bool Receive { get; set; } = true;
        public Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Receive ? ["operations.collections.work", "operations.collections.receive"] : ["operations.collections.work"]);
        public Task<SecurityData> ReadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsSystemAdministratorAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<T> ExecuteAsync<T>(Guid expectedRevision, Func<SecurityData, T> mutation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
