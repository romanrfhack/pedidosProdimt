using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class MachineRequirementTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, false, false)]
    public async Task Capture_FreezesExplicitRequirementAndNeverInheritsMachineWhenNotRequired(
        bool productRequiresMachine, bool relationshipRequiresMachine, bool expectedRequirement, bool expectedAssignment)
    {
        await using var f = await Fixture.Create();
        f.Product.RequiresMachine = productRequiresMachine;
        f.Preference.RequiresMachine = relationshipRequiresMachine;
        // An explicit preference must also be ignored when production is disabled.
        f.Preference.PreferredMachineId = f.Machine.Id;
        await f.Db.SaveChangesAsync();

        var response = await f.Orders.SubmitAsync(f.Customer.Id, new([new(f.Product.Id, 12.5m, null)]), Ct);
        f.Db.ChangeTracker.Clear();
        var line = await f.Db.OrderLines.SingleAsync();
        Assert.Equal(expectedRequirement, line.RequiresMachine);
        Assert.Equal(expectedAssignment ? f.Machine.Id : (Guid?)null, line.AssignedMachineId);
        var clientJson = JsonSerializer.Serialize(response);
        Assert.DoesNotContain("Machine", clientJson);

        // Later profile changes do not rewrite the frozen requirement or assignment.
        var profile = await f.Db.CustomerFrequentProducts.SingleAsync();
        profile.RequiresMachine = !relationshipRequiresMachine;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal(expectedRequirement, (await f.Db.OrderLines.SingleAsync()).RequiresMachine);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task NullMachine_IsStillRequiredAndInheritanceRemainsAnIndependentChoice(bool inherit, bool expectedAssignment)
    {
        await using var f = await Fixture.Create();
        f.Preference.PreferredMachineId = null;
        f.Preference.UseCustomerDefaultMachine = inherit;
        await f.Db.SaveChangesAsync();
        await f.Orders.SubmitAsync(f.Customer.Id, new([new(f.Product.Id, 5m, null)]), Ct);
        var line = await f.Db.OrderLines.SingleAsync();
        Assert.True(line.RequiresMachine);
        Assert.Equal(expectedAssignment ? f.Machine.Id : (Guid?)null, line.AssignedMachineId);
    }

    [Fact]
    public async Task ProfileRequest_OmittingRequirementPreservesExplicitNoMachineAndClearsInheritance()
    {
        await using var f = await Fixture.Create();
        f.Preference.RequiresMachine = false;
        f.Preference.UseCustomerDefaultMachine = false;
        await f.Db.SaveChangesAsync();
        var response = await f.Catalog.ReplaceFrequentProductsAsync(f.Customer.Id,
            new([new(f.Product.Id, 5m, 1, true, f.Machine.Id)]), null, Ct);
        var row = Assert.Single(response);
        Assert.False(row.RequiresMachine);
        Assert.Null(row.PreferredMachineId);
        Assert.False(row.UseCustomerDefaultMachine);
        f.Db.ChangeTracker.Clear();
        var saved = await f.Db.CustomerFrequentProducts.SingleAsync();
        Assert.False(saved.RequiresMachine);
    }

    [Fact]
    public async Task ProductRequest_OmittingRequirementPreservesFalse_AndProfileReportsCatalogConfiguration()
    {
        await using var f = await Fixture.Create();
        f.Product.RequiresMachine = false;
        await f.Db.SaveChangesAsync();
        var result = await f.Products.UpdateAsync(f.Product.Id, new("Producto de prueba actualizado", null, "kg"), null, Ct);
        Assert.False(result.RequiresMachine);
        var profile = await f.Catalog.GetOrderingProfileAsync(f.Customer.Id, Ct);
        Assert.True(Assert.Single(profile.Items).RequiresMachine);
        Assert.False(Assert.Single(profile.Items).ProductRequiresMachine);
    }

    [Fact]
    public void HistoricalDefaults_DoNotInferRequirementFromMissingMachine()
    {
        Assert.True(new Product().RequiresMachine);
        Assert.True(new CustomerFrequentProduct().RequiresMachine);
        Assert.True(new OrderLine { AssignedMachineId = null }.RequiresMachine);
        Assert.Null(new UpsertAdminProductRequest("Prueba", null).RequiresMachine);
        Assert.Null(new UpdateCustomerFrequentProductItemRequest(Guid.NewGuid(), null, 1, true).RequiresMachine);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consolidation_RejectsSourceOrTargetWithPhysicalProductionEvenWithoutAllocations(bool targetHasProduction)
    {
        await using var f = await Fixture.Create();
        var other = new Product { Id = Guid.NewGuid(), Name = "Producto de prueba equivalente", UnitCode = "kg" };
        f.Db.Products.Add(other);
        f.Db.ProductionBatches.Add(new()
        {
            Id = Guid.NewGuid(), ProductId = targetHasProduction ? f.Product.Id : other.Id,
            ProductNameSnapshot = "Producción de prueba", MachineId = f.Machine.Id,
            ServiceDate = new Clock().Today, UnitCode = "kg", Quantity = 7m, CreatedAt = new Clock().Now
        });
        await f.Db.SaveChangesAsync();
        var merge = new ProductConsolidationService(new EfProductRepository(f.Db), new EfCustomerRepository(f.Db),
            new EfCommercialCatalogRepository(f.Db), new EfAuditLogRepository(f.Db), new Clock(),
            production: new EfProductionRepository(f.Db));
        var preview = await merge.PreviewAsync(new(f.Product.Id, [other.Id], "Producto compartido de prueba"), Ct);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, x => x.Contains("producción registrada", StringComparison.Ordinal));
        Assert.Null(other.MergedIntoProductId);
        Assert.Empty(await f.Db.ProductionAllocations.ToArrayAsync());
        Assert.Equal(7m, (await f.Db.ProductionBatches.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task NoMachineConfiguration_RejectsProductWithRecordedUnallocatedProduction()
    {
        await using var f = await Fixture.Create();
        f.Db.ProductionBatches.Add(new()
        {
            Id = Guid.NewGuid(), ProductId = f.Product.Id, ProductNameSnapshot = f.Product.Name,
            MachineId = f.Machine.Id, ServiceDate = new Clock().Today, UnitCode = "kg",
            Quantity = 7m, CreatedAt = new Clock().Now
        });
        await f.Db.SaveChangesAsync();
        var service = new AdminProductCatalogService(new EfProductRepository(f.Db), new EfAuditLogRepository(f.Db),
            new Clock(), new EfCustomerRepository(f.Db), new EfProductionRepository(f.Db));
        var conflict = await Assert.ThrowsAsync<CatalogConflictException>(() => service.UpdateAsync(f.Product.Id,
            new(f.Product.Name, null, "kg", RequiresMachine: false), null, Ct));
        Assert.Contains("producción registrada", conflict.Message);
        Assert.True(f.Product.RequiresMachine);
        Assert.Equal(7m, (await f.Db.ProductionBatches.SingleAsync()).Quantity);
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
        public Product Product { get; } = new() { Id = Guid.NewGuid(), Name = "Producto de prueba", UnitCode = "kg" };
        public Customer Customer { get; } = new() { Id = Guid.NewGuid(), Name = "Cliente de prueba", OrderingProfileVersion = 1 };
        public Machine Machine { get; } = Machine.Create(19, "Máquina de prueba");
        public CustomerFrequentProduct Preference { get; }
        public CustomerOrderService Orders { get; }
        public AdminCustomerCatalogService Catalog { get; }
        public AdminProductCatalogService Products { get; }

        private Fixture(SqliteConnection connection, PedidosDbContext db)
        {
            this.connection = connection; Db = db;
            Preference = new() { CustomerId = Customer.Id, ProductId = Product.Id, SortOrder = 1 };
            var customers = new EfCustomerRepository(db); var products = new EfProductRepository(db);
            var machines = new EfMachineRepository(db); var audit = new EfAuditLogRepository(db); var clock = new Clock();
            Orders = new(customers, products, new EfSalesChannelRepository(db), new EfOrderRepository(db),
                new EfOrderAuditLogRepository(db), clock, machines);
            Catalog = new(customers, products, machines, audit, clock, new EfDeliveryRepository(db));
            Products = new(products, audit, clock, customers);
        }

        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var f = new Fixture(connection, db);
            db.Products.Add(f.Product); db.Customers.Add(f.Customer); db.Machines.Add(f.Machine);
            db.CustomerFrequentProducts.Add(f.Preference);
            db.CustomerMachineAssignments.Add(new() { CustomerId = f.Customer.Id, MachineId = f.Machine.Id, IsActive = true, IsDefault = true });
            db.SalesChannels.Add(new() { Id = Guid.NewGuid(), Name = "Cliente de prueba", Type = SalesChannelType.Customer });
            await db.SaveChangesAsync(); return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
