using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminImports;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class AdminImportServiceTests
{
    [Fact]
    public void CsvParser_HandlesHeadersQuotesCommasAndEmptyFields()
    {
        var parser = new CsvImportParser();

        var result = parser.Parse("name,description,isActive\n\"Molde, especial\",\"Dice \"\"demo\"\"\",");

        Assert.Equal(["name", "description", "isActive"], result.Headers);
        Assert.Single(result.Rows);
        Assert.Equal("Molde, especial", result.Rows[0].Get("name"));
        Assert.Equal("Dice \"demo\"", result.Rows[0].Get("description"));
        Assert.Equal(string.Empty, result.Rows[0].Get("isActive"));
    }

    [Fact]
    public async Task ValidateCustomers_DetectsRequiredNameAndDuplicates()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var csv = string.Join('\n',
        [
            "externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes",
            "C-001,,555,true,,,,",
            "C-002,Cliente Duplicado,555,true,,,,",
            "C-002,Cliente Duplicado 2,555,true,,,,"
        ]);

        var response = await fixture.Import.ValidateAsync(
            AdminImportTypes.Customers,
            new ImportCsvRequest(csv, "customers.csv"),
            CancellationToken.None);

        Assert.Equal(2, response.ErrorCount);
        Assert.Contains(response.Errors, x => x.Code == "Required" && x.Field == "name");
        Assert.Contains(response.Errors, x => x.Code == "DuplicateCustomer");
    }

    [Fact]
    public async Task ApplyCustomers_CreatesCustomerAndAuditsBulkImport()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var csv = string.Join('\n',
        [
            "externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes",
            "C-100,Cliente Importado,5550001000,true,09:30,,,Entrega demo"
        ]);

        var response = await fixture.Import.ApplyAsync(
            AdminImportTypes.Customers,
            new ImportCsvRequest(csv, "customers.csv"),
            actor: null,
            CancellationToken.None);

        var customer = await fixture.DbContext.Customers.SingleAsync(x => x.ExternalCode == "C-100");
        Assert.Equal(1, response.CreatedCount);
        Assert.Equal("Cliente Importado", customer.Name);
        Assert.Equal(new TimeOnly(9, 30), customer.PreferredDeliveryTime);
        Assert.True(await fixture.DbContext.AuditLogs.AnyAsync(x => x.EventType == "BulkImportApplied"));
    }

    [Fact]
    public async Task ApplyCustomers_UpdatesExistingCustomerByNameFallback()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var csv = string.Join('\n',
        [
            "externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes",
            "GT-IMPORT,Gran Takito,5559990000,true,08:45,,,Actualizado por importacion"
        ]);

        var response = await fixture.Import.ApplyAsync(
            AdminImportTypes.Customers,
            new ImportCsvRequest(csv, "customers.csv"),
            actor: null,
            CancellationToken.None);

        var customer = await fixture.DbContext.Customers.SingleAsync(x => x.Id == DevelopmentSeedIds.GranTakitoCustomerId);
        Assert.Equal(1, response.UpdatedCount);
        Assert.Equal("GT-IMPORT", customer.ExternalCode);
        Assert.Equal("5559990000", customer.PhoneNumber);
        Assert.Equal(new TimeOnly(8, 45), customer.PreferredDeliveryTime);
    }

    [Fact]
    public async Task ValidateProducts_DetectsRequiredName()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var csv = string.Join('\n',
        [
            "externalCode,name,description,isActive",
            "P-001,,Producto sin nombre,true"
        ]);

        var response = await fixture.Import.ValidateAsync(
            AdminImportTypes.Products,
            new ImportCsvRequest(csv, "products.csv"),
            CancellationToken.None);

        Assert.Contains(response.Errors, x => x.Code == "Required" && x.Field == "name");
    }

    [Fact]
    public async Task ApplyProducts_CreatesProduct()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var csv = string.Join('\n',
        [
            "externalCode,name,description,isActive",
            "P-100,Molde Importado,Producto demo,true"
        ]);

        var response = await fixture.Import.ApplyAsync(
            AdminImportTypes.Products,
            new ImportCsvRequest(csv, "products.csv"),
            actor: null,
            CancellationToken.None);

        Assert.Equal(1, response.CreatedCount);
        Assert.True(await fixture.DbContext.Products.AnyAsync(x =>
            x.ExternalCode == "P-100" &&
            x.Name == "Molde Importado"));
    }

    [Fact]
    public async Task ApplyMachines_CreatesMachine()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var csv = string.Join('\n',
        [
            "externalCode,number,name,isActive",
            "M-100,100,Maquina Importada,true"
        ]);

        var response = await fixture.Import.ApplyAsync(
            AdminImportTypes.Machines,
            new ImportCsvRequest(csv, "machines.csv"),
            actor: null,
            CancellationToken.None);

        Assert.Equal(1, response.CreatedCount);
        Assert.True(await fixture.DbContext.Machines.AnyAsync(x =>
            x.ExternalCode == "M-100" &&
            x.Number == 100));
    }

    [Fact]
    public async Task FrequentProducts_ValidateMissingReferencesAndNegativeQuantity()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var missingReferencesCsv = string.Join('\n',
        [
            "customerExternalCode,customerName,productExternalCode,productName,defaultQuantity,sortOrder,isActive",
            ",Cliente Inexistente,,Producto Inexistente,5,1,true"
        ]);
        var negativeQuantityCsv = string.Join('\n',
        [
            "customerExternalCode,customerName,productExternalCode,productName,defaultQuantity,sortOrder,isActive",
            ",Gran Takito,,#9 1/2,-1,1,true"
        ]);

        var missingResponse = await fixture.Import.ValidateAsync(
            AdminImportTypes.CustomerFrequentProducts,
            new ImportCsvRequest(missingReferencesCsv, "frequent.csv"),
            CancellationToken.None);
        var negativeResponse = await fixture.Import.ValidateAsync(
            AdminImportTypes.CustomerFrequentProducts,
            new ImportCsvRequest(negativeQuantityCsv, "frequent.csv"),
            CancellationToken.None);

        Assert.Contains(missingResponse.Errors, x => x.Code == "CustomerNotFound");
        Assert.Contains(missingResponse.Errors, x => x.Code == "ProductNotFound");
        Assert.Contains(negativeResponse.Errors, x => x.Code == "NegativeQuantity");
    }

    [Fact]
    public async Task ApplyFrequentProducts_ReplacesOnlyCustomersPresentInFile()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var csv = string.Join('\n',
        [
            "customerExternalCode,customerName,productExternalCode,productName,defaultQuantity,sortOrder,isActive",
            ",Gran Takito,,#11,4,1,true"
        ]);

        var response = await fixture.Import.ApplyAsync(
            AdminImportTypes.CustomerFrequentProducts,
            new ImportCsvRequest(csv, "frequent.csv"),
            actor: null,
            CancellationToken.None);

        var granTakitoFrequent = await fixture.DbContext.CustomerFrequentProducts
            .Where(x => x.CustomerId == DevelopmentSeedIds.GranTakitoCustomerId)
            .ToArrayAsync();
        var demo2Frequent = await fixture.DbContext.CustomerFrequentProducts
            .Where(x => x.CustomerId == DevelopmentSeedIds.DemoCustomer2Id)
            .ToArrayAsync();

        Assert.Equal(1, response.UpdatedCount);
        Assert.Single(granTakitoFrequent);
        Assert.Equal(DevelopmentSeedIds.ProductElevenId, granTakitoFrequent[0].ProductId);
        Assert.NotEmpty(demo2Frequent);
    }

    [Fact]
    public async Task MachineAssignments_RejectMultipleDefaultsAndInactiveDefault()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var multipleDefaultsCsv = string.Join('\n',
        [
            "customerExternalCode,customerName,machineExternalCode,machineNumber,isDefault,notes",
            ",Gran Takito,,1,true,Default 1",
            ",Gran Takito,,2,true,Default 2"
        ]);

        var multipleDefaults = await fixture.Import.ValidateAsync(
            AdminImportTypes.CustomerMachineAssignments,
            new ImportCsvRequest(multipleDefaultsCsv, "assignments.csv"),
            CancellationToken.None);

        fixture.DbContext.Machines.Single(x => x.Id == DevelopmentSeedIds.MachineTwoId).Deactivate();
        await fixture.DbContext.SaveChangesAsync();
        var inactiveDefaultCsv = string.Join('\n',
        [
            "customerExternalCode,customerName,machineExternalCode,machineNumber,isDefault,notes",
            ",Gran Takito,,2,true,Default inactivo"
        ]);
        var inactiveDefault = await fixture.Import.ValidateAsync(
            AdminImportTypes.CustomerMachineAssignments,
            new ImportCsvRequest(inactiveDefaultCsv, "assignments.csv"),
            CancellationToken.None);

        Assert.Contains(multipleDefaults.Errors, x => x.Code == "MultipleDefaultMachines");
        Assert.Contains(inactiveDefault.Errors, x => x.Code == "InactiveMachineCannotBeDefault");
    }

    [Fact]
    public async Task ApplyMachineAssignments_DoesNotExposeMachineToCustomerResponse()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var csv = string.Join('\n',
        [
            "customerExternalCode,customerName,machineExternalCode,machineNumber,isDefault,notes",
            ",Gran Takito,,2,true,Asignacion importada"
        ]);

        await fixture.Import.ApplyAsync(
            AdminImportTypes.CustomerMachineAssignments,
            new ImportCsvRequest(csv, "assignments.csv"),
            actor: null,
            CancellationToken.None);

        var today = await fixture.CustomerOrders.GetTodayAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);
        var payload = JsonSerializer.Serialize(today, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("machine", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maquina", payload, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ImportFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ImportFixture(
            SqliteConnection connection,
            PedidosDbContext dbContext,
            AdminImportService import,
            CustomerOrderService customerOrders)
        {
            _connection = connection;
            DbContext = dbContext;
            Import = import;
            CustomerOrders = customerOrders;
        }

        public PedidosDbContext DbContext { get; }

        public AdminImportService Import { get; }

        public CustomerOrderService CustomerOrders { get; }

        public static async Task<ImportFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<PedidosDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new PedidosDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            await PedidosDevelopmentSeeder.SeedAsync(dbContext);

            var customerRepository = new EfCustomerRepository(dbContext);
            var productRepository = new EfProductRepository(dbContext);
            var machineRepository = new EfMachineRepository(dbContext);
            var orderRepository = new EfOrderRepository(dbContext);
            var orderAuditRepository = new EfOrderAuditLogRepository(dbContext);
            var auditLogRepository = new EfAuditLogRepository(dbContext);
            var salesChannelRepository = new EfSalesChannelRepository(dbContext);
            var dateTimeProvider = new FixedDateTimeProvider(new DateTimeOffset(2026, 5, 28, 9, 0, 0, TimeSpan.Zero));

            var import = new AdminImportService(
                new CsvImportParser(),
                customerRepository,
                productRepository,
                machineRepository,
                auditLogRepository,
                dateTimeProvider);
            var customerOrders = new CustomerOrderService(
                customerRepository,
                productRepository,
                salesChannelRepository,
                orderRepository,
                orderAuditRepository,
                dateTimeProvider);

            return new ImportFixture(connection, dbContext, import, customerOrders);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset Now { get; } = now;

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
    }
}
