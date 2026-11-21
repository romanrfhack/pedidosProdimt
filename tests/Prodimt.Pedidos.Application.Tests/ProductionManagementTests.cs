using Microsoft.Data.Sqlite;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Api;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Security;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class ProductionManagementTests
{
    private static readonly CancellationToken Ct = default;
    private static readonly AdminActorContext Actor = new("production-test", "Responsable de prueba");

    [Fact]
    public async Task PhysicalProductionIsIndependentFromAllocationsAndRouteFilters()
    {
        await using var fixture = await Fixture.CreateAsync();
        var batch = await fixture.Service.RegisterAsync(new(Guid.NewGuid(), fixture.Date, fixture.Machine.Id,
            fixture.Product.Id, 30), Actor, Ct);
        await fixture.Service.AllocateAsync(new(Guid.NewGuid(), batch.Id, fixture.Order.Id, fixture.Line.Id, 7, 1), Actor, Ct);
        var dashboard = await fixture.Service.GetDashboardAsync(fixture.Date, "R-ONE", Ct);
        var machine = Assert.Single(dashboard.Machines);
        Assert.Equal("all-routes", dashboard.SummaryScope);
        Assert.Single(dashboard.Orders);
        Assert.Equal(35, machine.AssignedQuantity);
        Assert.Equal(30, machine.ProducedQuantity);
        Assert.Equal(5, machine.PendingQuantity);
        Assert.Equal(7, machine.AllocatedQuantity);
        Assert.Equal(23, machine.AvailableQuantity);
        Assert.Equal(13, dashboard.Orders.Single().Lines.Single().PendingAllocationQuantity);
    }

    [Fact]
    public async Task SurplusOfOneProductNeverCompensatesDemandOfAnotherProduct()
    {
        await using var fixture = await Fixture.CreateAsync();
        var second = Product.Create("Producto diferente", null); second.SetUnitCode("kg");
        fixture.Db.Products.Add(second);
        var customer = await fixture.Db.Customers.SingleAsync(x => x.Id == fixture.Order.CustomerId);
        fixture.Db.Orders.Add(Order.CreateSubmitted(customer.Id, fixture.Order.SalesChannelId, fixture.Date,
            fixture.Clock.Now, 3, new(false, false, null, OrderStatus.Submitted),
            [new OrderLine { Id = Guid.NewGuid(), ProductId = second.Id, ProductNameSnapshot = second.Name,
                UnitCode = "kg", Quantity = 8, AssignedMachineId = fixture.Machine.Id }], customer));
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.RegisterAsync(new(Guid.NewGuid(), fixture.Date, fixture.Machine.Id,
            fixture.Product.Id, 100), Actor, Ct);
        var machine = Assert.Single((await fixture.Service.GetDashboardAsync(fixture.Date, null, Ct)).Machines);
        Assert.Equal(43, machine.AssignedQuantity);
        Assert.Equal(100, machine.ProducedQuantity);
        Assert.Equal(8, machine.PendingQuantity);
    }

    [Fact]
    public async Task ReplayedCommandsKeepOnePhysicalBatchAndOneAllocation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var registration = new RegisterProductionRequest(Guid.NewGuid(), fixture.Date, fixture.Machine.Id, fixture.Product.Id, 20);
        var first = await fixture.Service.RegisterAsync(registration, Actor, Ct);
        var replay = await fixture.Service.RegisterAsync(registration, Actor, Ct);
        Assert.Equal(first.Id, replay.Id); Assert.True(replay.Replayed);
        var assignment = new AllocateProductionRequest(Guid.NewGuid(), first.Id, fixture.Order.Id, fixture.Line.Id, 8, 1);
        var assigned = await fixture.Service.AllocateAsync(assignment, Actor, Ct);
        var assignedReplay = await fixture.Service.AllocateAsync(assignment, Actor, Ct);
        Assert.Equal(assigned.Id, assignedReplay.Id); Assert.True(assignedReplay.Replayed);
        Assert.Equal(1, await fixture.Db.ProductionBatches.CountAsync());
        Assert.Equal(1, await fixture.Db.ProductionAllocations.CountAsync());
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.Service.RegisterAsync(registration with { Quantity = 21 }, Actor, Ct));
    }

    [Fact]
    public async Task AllocationCannotExceedTheBatchOrTheRemainingOrderDemand()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.RegisterAsync(new(Guid.NewGuid(), fixture.Date, fixture.Machine.Id, fixture.Product.Id, 5), Actor, Ct);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.Service.AllocateAsync(new(Guid.NewGuid(), first.Id,
            fixture.Order.Id, fixture.Line.Id, 6, 1), Actor, Ct));
        Assert.Equal(0, await fixture.Db.ProductionAllocations.CountAsync());
        var second = await fixture.Service.RegisterAsync(new(Guid.NewGuid(), fixture.Date, fixture.Machine.Id, fixture.Product.Id, 40), Actor, Ct);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.Service.AllocateAsync(new(Guid.NewGuid(), second.Id,
            fixture.Order.Id, fixture.Line.Id, 21, 1), Actor, Ct));
        Assert.Equal(0, await fixture.Db.ProductionAllocations.CountAsync());
    }

    [Fact]
    public async Task ReversalRestoresAvailabilityAndRetainsAuditAndOriginalAssignment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var batch = await fixture.Service.RegisterAsync(new(Guid.NewGuid(), fixture.Date, fixture.Machine.Id, fixture.Product.Id, 20), Actor, Ct);
        var assigned = await fixture.Service.AllocateAsync(new(Guid.NewGuid(), batch.Id, fixture.Order.Id, fixture.Line.Id, 9, 1), Actor, Ct);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.Service.AssignMachineAsync(fixture.Order.Id, fixture.Line.Id,
            new(Guid.NewGuid(), null, 2, "Cambiar máquina"), Actor, Ct));
        var reverseRequest = new ReverseProductionAllocationRequest(Guid.NewGuid(), "Corregir destino del lote");
        var reversed = await fixture.Service.ReverseAsync(assigned.Id, reverseRequest, Actor, Ct);
        Assert.Equal(3, reversed.OrderVersion);
        var dashboard = await fixture.Service.GetDashboardAsync(fixture.Date, null, Ct);
        Assert.Equal(20, dashboard.Machines.Single().AvailableQuantity);
        var link = Assert.Single(dashboard.Allocations);
        Assert.NotNull(link.ReversedAt); Assert.Equal(reverseRequest.Reason, link.ReverseReason);
        Assert.Equal(3, await fixture.Db.AuditLogs.CountAsync());
        Assert.True((await fixture.Service.ReverseAsync(assigned.Id, reverseRequest, Actor, Ct)).Replayed);
        await fixture.Service.AssignMachineAsync(fixture.Order.Id, fixture.Line.Id,
            new(Guid.NewGuid(), null, 3, "Replanear pendiente"), Actor, Ct);
        Assert.Null((await fixture.Service.GetDashboardAsync(fixture.Date, null, Ct)).Orders
            .Single(x => x.OrderId == fixture.Order.Id).Lines.Single().AssignedMachineId);
    }

    [Fact]
    public async Task NoMachineProductsDisappearFromProductionPlansAndCannotReceiveKilos()
    {
        await using var fixture = await Fixture.CreateAsync();
        var nonMachine = Product.Create("Reventa", null); nonMachine.SetUnitCode("kg"); nonMachine.RequiresMachine = false;
        fixture.Db.Products.Add(nonMachine);
        var customer = await fixture.Db.Customers.SingleAsync(x => x.Id == fixture.Order.CustomerId);
        var line = new OrderLine { Id = Guid.NewGuid(), ProductId = nonMachine.Id, ProductNameSnapshot = nonMachine.Name,
            UnitCode = "kg", Quantity = 100, RequiresMachine = false };
        var order = Order.CreateSubmitted(customer.Id, fixture.Order.SalesChannelId, fixture.Date, fixture.Clock.Now,
            3, new(false, false, null, OrderStatus.Submitted), [line], customer);
        fixture.Db.Orders.Add(order); await fixture.Db.SaveChangesAsync();
        var dashboard = await fixture.Service.GetDashboardAsync(fixture.Date, null, Ct);
        Assert.DoesNotContain(dashboard.Orders, x => x.OrderId == order.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.RegisterAsync(new(Guid.NewGuid(), fixture.Date,
            fixture.Machine.Id, nonMachine.Id, 1), Actor, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.AssignMachineAsync(order.Id, line.Id,
            new(Guid.NewGuid(), fixture.Machine.Id, 1, "Intento inválido"), Actor, Ct));
        var plan = await new AdminOperationsService(new EfOrderRepository(fixture.Db), new EfCustomerRepository(fixture.Db),
            new EfMachineRepository(fixture.Db), fixture.Clock, new EfProductRepository(fixture.Db)).GetPlanAsync(fixture.Date, null, Ct);
        Assert.DoesNotContain(plan.Orders, x => x.OrderId == order.Id);
        Assert.DoesNotContain(plan.Warnings, x => x.OrderId == order.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MachineAssignmentPublishesBothThePreviousAndNewDemand(bool previouslyAssigned)
    {
        await using var fixture = await Fixture.CreateAsync();
        var nextMachine = new Machine { Id = Guid.NewGuid(), Number = 2, Name = "Otra máquina", IsActive = true };
        fixture.Db.Machines.Add(nextMachine);
        var order = await fixture.Db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == fixture.Order.Id);
        if (!previouslyAssigned) order.Lines.Single().AssignedMachineId = null;
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.AssignMachineAsync(order.Id, fixture.Line.Id,
            new(Guid.NewGuid(), nextMachine.Id, 1, "Redistribuir carga"), Actor, Ct);
        var notices = await fixture.Db.OrderChangeNotices.Where(x => x.OrderId == order.Id).ToArrayAsync();
        Assert.Equal(2, notices.Length);
        var previous = Assert.Single(notices, x => x.MachineId == (previouslyAssigned ? fixture.Machine.Id : null));
        var next = Assert.Single(notices, x => x.MachineId == nextMachine.Id);
        var previousLine = Assert.Single(JsonSerializer.Deserialize<OrderChangeLineResponse[]>(previous.LinesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        var nextLine = Assert.Single(JsonSerializer.Deserialize<OrderChangeLineResponse[]>(next.LinesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        Assert.Equal(-20, previousLine.DeltaQuantity); Assert.Equal(0, previousLine.AfterQuantity);
        Assert.Equal(20, nextLine.DeltaQuantity); Assert.Equal(0, nextLine.BeforeQuantity);
        Assert.All(notices, notice => Assert.Equal(2, notice.OrderVersion));
        Assert.Equal(0, await fixture.Db.ProductionBatches.CountAsync());
    }

    [Fact]
    public async Task SnapshotFalseDoesNotPublishButHistoricalRemovalsRemainOutsideTheCurrentPlan()
    {
        await using var fixture = await Fixture.CreateAsync();
        var order = await fixture.Db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == fixture.Order.Id);
        order.Lines.Single().RequiresMachine = false;
        order.Lines.Single().AssignedMachineId = null;
        await fixture.Db.SaveChangesAsync();
        var publisher = new OrderChangeNoticePublisher(new EfOrderChangeNoticeRepository(fixture.Db),
            new EfMachineRepository(fixture.Db), new EfCustomerRepository(fixture.Db));
        var before = OrderChangeNoticePublisher.Capture(order);
        Assert.Empty(before);
        order.Lines.Single().Notes = "Cambió preparación sin máquina";
        order.Version++;
        await publisher.PublishAsync(order, before, "correction", "Sin producción", Actor, fixture.Clock.Now, Ct);
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(await fixture.Db.OrderChangeNotices.ToArrayAsync());
        // This earlier valid notice refers to retired demand. Its current order
        // has no required lines and is deliberately absent from the plan.
        fixture.Db.OrderChangeNotices.Add(new OrderChangeNotice
        {
            OrderId = order.Id, OrderVersion = order.Version, ServiceDate = fixture.Date,
            CustomerName = "Cliente prueba", MachineId = fixture.Machine.Id, MachineNumber = 1,
            Kind = "correction", Reason = "Retirar demanda anterior", OccurredAt = fixture.Clock.Now,
            LinesJson = JsonSerializer.Serialize(new[] { new OrderChangeLineResponse(fixture.Product.Id,
                fixture.Product.Name, "kg", 20, 0, -20, null, null) }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        var product = await fixture.Db.Products.SingleAsync(x => x.Id == fixture.Product.Id);
        product.RequiresMachine = false;
        await fixture.Db.SaveChangesAsync();
        var changes = await new OrderChangeNoticeService(new EfOrderChangeNoticeRepository(fixture.Db),
            new EfOrderOperationStore(fixture.Db), fixture.Clock, new EfProductRepository(fixture.Db))
            .GetAsync(fixture.Date, null, null, true, Ct);
        Assert.Equal(1, changes.PendingCount); Assert.Single(changes.Changes);
        var plan = await fixture.Service.GetDashboardAsync(fixture.Date, null, Ct);
        Assert.DoesNotContain(plan.Orders, x => x.OrderId == order.Id);
        Assert.Equal(-20, changes.Changes.Single().Lines.Single().DeltaQuantity);
    }

    [Fact]
    public async Task AllocationHistoryResolvesTheCustomerWhenTheOrderHasNoNameSnapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var order = await fixture.Db.Orders.SingleAsync(x => x.Id == fixture.Order.Id);
        order.CustomerNameSnapshot = null;
        await fixture.Db.SaveChangesAsync();
        var batch = await fixture.Service.RegisterAsync(new(Guid.NewGuid(), fixture.Date, fixture.Machine.Id, fixture.Product.Id, 20), Actor, Ct);
        var allocation = await fixture.Service.AllocateAsync(new(Guid.NewGuid(), batch.Id, order.Id, fixture.Line.Id, 1, 1), Actor, Ct);
        Assert.Equal("Cliente prueba", (await fixture.Db.ProductionAllocations.SingleAsync(x => x.Id == allocation.Id)).CustomerNameSnapshot);
    }

    [Fact]
    public async Task CancellationNoticeRemainsReceivableAfterTheOrderLeavesProduction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var order = await fixture.Db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == fixture.Order.Id);
        var publisher = new OrderChangeNoticePublisher(new EfOrderChangeNoticeRepository(fixture.Db),
            new EfMachineRepository(fixture.Db), new EfCustomerRepository(fixture.Db));
        var before = OrderChangeNoticePublisher.Capture(order);
        fixture.Db.Entry(order).Property(x => x.Status).CurrentValue = OrderStatus.Cancelled;
        order.Version++;
        await publisher.PublishAsync(order, before, "cancelled", "Cancelar demanda sin producción asignada", Actor, fixture.Clock.Now, Ct);
        await fixture.Db.SaveChangesAsync();
        Assert.DoesNotContain((await fixture.Service.GetDashboardAsync(fixture.Date, null, Ct)).Orders, x => x.OrderId == order.Id);
        var service = new OrderChangeNoticeService(new EfOrderChangeNoticeRepository(fixture.Db),
            new EfOrderOperationStore(fixture.Db), fixture.Clock, new EfProductRepository(fixture.Db));
        var notice = Assert.Single((await service.GetAsync(fixture.Date, null, null, true, Ct)).Changes);
        Assert.Equal(0, notice.Lines.Single().AfterQuantity);
        await service.AcknowledgeAsync(notice.Id, new("RECEIVE-CANCELLED-DEMAND", notice.Version), Actor, Ct);
        Assert.Empty((await service.GetAsync(fixture.Date, null, null, true, Ct)).Changes);
    }

    [Theory]
    [InlineData("/api/admin/operations/production/batches", "operations.produce")]
    [InlineData("/api/admin/operations/production/allocations", "operations.allocate")]
    [InlineData("/api/admin/operations/production/allocations/11111111-1111-1111-1111-111111111111/reverse", "operations.allocate")]
    [InlineData("/api/admin/operations/orders/11111111-1111-1111-1111-111111111111/lines/22222222-2222-2222-2222-222222222222/machine", "operations.assign")]
    [InlineData("/api/admin/operations/unknown-write", "__unmapped__")]
    public void ProductionWritesRequireTheirOwnPermission(string path, string expected)
        => Assert.Equal(expected, AdminPermissionMiddleware.RequiredPermission("POST", path));

    [Fact]
    public async Task WorkspaceCaptureReleaseAndHandoffKeepOneTraceablePhysicalBalance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (actor, access, repository) = await SeedMachineOperatorAsync(fixture);
        var machine = fixture.Machine.Id;
        var order = fixture.Order.Id;
        var line = fixture.Line.Id;

        var excessive = new MachineCaptureRequest(Guid.NewGuid(), order, line, 21, 1);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() =>
            fixture.Service.CaptureForMachineAsync(machine, excessive, access, actor, Ct));
        Assert.Equal(0, await fixture.Db.ProductionBatches.CountAsync());

        var capture = excessive with { OperationId = Guid.NewGuid(), Quantity = 18 };
        var assigned = await fixture.Service.CaptureForMachineAsync(machine, capture, access, actor, Ct);
        Assert.True((await fixture.Service.CaptureForMachineAsync(machine, capture, access, actor, Ct)).Replayed);
        Assert.Equal(1, await fixture.Db.ProductionBatches.CountAsync());
        Assert.Equal(1, await fixture.Db.ProductionAllocations.CountAsync());

        var complete = new MachineReleaseRequest(Guid.NewGuid(), order, null, null, 2);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() =>
            fixture.Service.ReleaseMachineProductionAsync(machine, complete, access, repository, actor, Ct));
        var partial = complete with { OperationId = Guid.NewGuid(), OrderLineId = line, Quantity = 5 };
        await fixture.Service.ReleaseMachineProductionAsync(machine, partial, access, repository, actor, Ct);
        Assert.True((await fixture.Service.ReleaseMachineProductionAsync(machine, partial, access, repository, actor, Ct)).Replayed);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() =>
            fixture.Service.ReverseAsync(assigned.Id, new(Guid.NewGuid(), "Corregir kilos"), Actor, Ct));

        var loose = await fixture.Service.RegisterMachineLotAsync(machine,
            new(Guid.NewGuid(), fixture.Product.Id, 7, fixture.Date), access, actor, Ct);
        var handoff = new MachineHandoffRequest(Guid.NewGuid(), loose.Id, 5);
        await fixture.Service.HandoffMachineLotAsync(machine, handoff, access, actor, Ct);
        Assert.True((await fixture.Service.HandoffMachineLotAsync(machine, handoff, access, actor, Ct)).Replayed);
        var allocation = new AllocateProductionRequest(Guid.NewGuid(), loose.Id, order, line, 3, 2);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() =>
            fixture.Service.AllocateAsync(allocation, Actor, Ct));
        await fixture.Service.AllocateMachineLotAsync(machine,
            allocation with { OperationId = Guid.NewGuid(), Quantity = 2 }, access, actor, Ct);
        await fixture.Service.ReleaseMachineProductionAsync(machine,
            complete with { OperationId = Guid.NewGuid(), ExpectedOrderVersion = 3 }, access, repository, actor, Ct);

        var view = await fixture.Service.GetMachineWorkspaceAsync(machine, access, repository, actor, Ct);
        Assert.Equal(new MachineWorkspaceTotals(25, 20, 15, 5), view.Totals);
        Assert.Equal(20, view.Orders.Single(x => x.OrderId == order).Lines.Single().ReleasedQuantity);
        Assert.Equal(2, view.Notices.Count);
        Assert.Equal(20, view.Notices.Sum(notice => notice.Quantity));
        var dashboard = await fixture.Service.GetDashboardAsync(fixture.Date, null, Ct);
        Assert.Equal(2, dashboard.Releases!.Count);
        Assert.All(dashboard.Releases, notice => Assert.Equal(machine, notice.MachineId));
        var excluded = await fixture.Service.GetDashboardAsync(fixture.Date, "R-TWO", Ct);
        Assert.Empty(excluded.Releases!);
        var looseBatch = view.Batches.Single(x => x.Id == loose.Id);
        Assert.Equal(5, looseBatch.HandedOffQuantity);
        Assert.Equal(5, looseBatch.AvailableQuantity);
    }

    [Fact]
    public async Task WorkspaceAssignmentIsRecheckedBeforeReplayingTheReceipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (actor, access, repository) = await SeedMachineOperatorAsync(fixture);
        var other = new Machine { Id = Guid.NewGuid(), Number = 2, IsActive = true };
        fixture.Db.Machines.Add(other);
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.GetMachineWorkspaceAsync(other.Id, access, repository, actor, Ct));

        var request = new MachineCaptureRequest(Guid.NewGuid(), fixture.Order.Id, fixture.Line.Id, 4, 1);
        await fixture.Service.CaptureForMachineAsync(fixture.Machine.Id, request, access, actor, Ct);
        await repository.ReplaceOperatorsAsync(fixture.Machine.Id, [], Ct);
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.CaptureForMachineAsync(fixture.Machine.Id, request, access, actor, Ct));
    }

    private static async Task<(AdminActorContext Actor, MachineWorkspaceAccess Access, IMachineWorkspaceRepository Repository)>
        SeedMachineOperatorAsync(Fixture fixture)
    {
        var user = AdminUser.Create("maquina.prueba", "Maquinista", "test-hash", fixture.Clock.Now);
        var permission = SecurityDefaults.Permissions.Single(x => x.Key == MachineWorkspaceAccess.WorkPermission);
        fixture.Db.AddRange(user, permission,
            new SecurityUserPermission { UserId = user.Id, PermissionId = permission.Id },
            new MachineOperator { MachineId = fixture.Machine.Id, UserId = user.Id });
        await fixture.Db.SaveChangesAsync();
        var repository = new EfMachineWorkspaceRepository(fixture.Db);
        return (new(user.Id.ToString(), user.DisplayName),
            new(new EfSecurityStore(fixture.Db), new EfMachineRepository(fixture.Db), repository), repository);
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset Now => new(2026, 11, 2, 14, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 11, 2);
        public TimeOnly LocalTimeOfDay => new(8, 0);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public PedidosDbContext Db { get; }
        public Clock Clock { get; } = new();
        public DateOnly Date => Clock.Today;
        public Product Product { get; }
        public Machine Machine { get; }
        public Order Order { get; }
        public OrderLine Line => Order.Lines.Single();
        public ProductionManagementService Service { get; }
        private Fixture(SqliteConnection connection, PedidosDbContext db, Product product, Machine machine, Order order)
        {
            this.connection = connection; Db = db; Product = product; Machine = machine; Order = order;
            Service = new(new EfProductionRepository(db), new EfOrderRepository(db), new EfProductRepository(db),
                new EfMachineRepository(db), new EfCustomerRepository(db), new EfOrderOperationStore(db), new EfAuditLogRepository(db), Clock,
                new OrderChangeNoticePublisher(new EfOrderChangeNoticeRepository(db), new EfMachineRepository(db), new EfCustomerRepository(db)), new EfMachineWorkspaceRepository(db));
        }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var clock = new Clock();
            var product = Product.Create("Producto de prueba", null); product.SetUnitCode("kg");
            var machine = new Machine { Id = Guid.NewGuid(), Number = 1, Name = "Máquina prueba", IsActive = true };
            var customer = Customer.Create("Cliente prueba", null, null, null, null, null, clock.Now);
            customer.SetPilotDelivery("R-ONE", 1, null, null, null, clock.Now);
            var channel = new SalesChannel { Id = Guid.NewGuid(), Name = "Prueba", Type = SalesChannelType.AdminManualCapture };
            var order = Order.CreateSubmitted(customer.Id, channel.Id, clock.Today, clock.Now, 1,
                new(false, false, null, OrderStatus.Submitted), [new OrderLine { Id = Guid.NewGuid(), ProductId = product.Id,
                    ProductNameSnapshot = product.Name, UnitCode = "kg", Quantity = 20, AssignedMachineId = machine.Id }], customer);
            var other = Order.CreateSubmitted(customer.Id, channel.Id, clock.Today, clock.Now, 2,
                new(false, false, null, OrderStatus.Submitted), [new OrderLine { Id = Guid.NewGuid(), ProductId = product.Id,
                    ProductNameSnapshot = product.Name, UnitCode = "kg", Quantity = 15, AssignedMachineId = machine.Id }], customer);
            other.RouteCode = "R-TWO";
            db.AddRange(product, machine, customer, channel, order, other); await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new(connection, db, product, machine, order);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
