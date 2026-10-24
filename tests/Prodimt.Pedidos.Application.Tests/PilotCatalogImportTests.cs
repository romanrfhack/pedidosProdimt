using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminImports;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class PilotCatalogImportTests
{
    private const string CustomerHeader = "externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes";
    private const string ProductHeader = "externalCode,name,description,isActive";
    private const string FrequentHeader = "customerExternalCode,customerName,productExternalCode,productName,defaultQuantity,sortOrder,isActive";

    [Fact]
    public async Task LegacyCustomerImport_PreservesPrivatePilotFields_AndNewCustomerStaysUnknown()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Customer.SetPilotDelivery("PILOT-DEMO", 1, true, "cash_on_delivery", "Instruccion privada de prueba", fixture.Now);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Apply(AdminImportTypes.Customers,
            $"{CustomerHeader}\nC-TEST,Cliente Demo Editado,,true,,,,\nC-NEW,Cliente Nuevo,,true,,,,");

        Assert.Empty(result.Errors);
        fixture.Db.ChangeTracker.Clear();
        var existing = await fixture.Db.Customers.SingleAsync(x => x.ExternalCode == "C-TEST");
        Assert.Equal("PILOT-DEMO", existing.RouteCode);
        Assert.Equal(1, existing.RouteStopOrder);
        Assert.True(existing.HasStoreKey);
        Assert.Equal("cash_on_delivery", existing.PaymentTermCode);
        Assert.Equal("Instruccion privada de prueba", existing.InternalDeliveryInstructions);
        var added = await fixture.Db.Customers.SingleAsync(x => x.ExternalCode == "C-NEW");
        Assert.Null(added.HasStoreKey);
        Assert.Null(added.PaymentTermCode);
        Assert.Null(added.RouteCode);
    }

    [Fact]
    public async Task PresentBlankPilotColumns_ClearToUnknown_WithoutInventingWeeklyCredit()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Customer.SetPilotDelivery("PILOT-DEMO", 1, true, "cash_on_delivery", "Instruccion privada", fixture.Now);
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Apply(AdminImportTypes.Customers,
            $"{CustomerHeader},routeCode,routeStopOrder,hasStoreKey,paymentTermCode,internalDeliveryInstructions\nC-TEST,Cliente Demo,,true,,,,,,,,,");
        Assert.Empty(result.Errors);
        fixture.Db.ChangeTracker.Clear();
        var customer = await fixture.Db.Customers.SingleAsync();
        Assert.Null(customer.RouteCode);
        Assert.Null(customer.RouteStopOrder);
        Assert.Null(customer.HasStoreKey);
        Assert.Null(customer.PaymentTermCode);
        Assert.Null(customer.InternalDeliveryInstructions);
    }

    [Fact]
    public async Task CustomerValidation_RejectsStopWithoutRoute_AndDoesNotLogPrivateInstructions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var noRoute = await fixture.Validate(AdminImportTypes.Customers,
            $"{CustomerHeader},routeStopOrder\nC-TEST,Cliente Demo,,true,,,,,2");
        Assert.Contains(noRoute.Errors, x => x.Code == "RouteRequiredForStop");
        var tooLong = await fixture.Validate(AdminImportTypes.Customers,
            $"{CustomerHeader},internalDeliveryInstructions\nC-TEST,Cliente Demo,,true,,,,,{new string('a', 1001)}");
        var issue = Assert.Single(tooLong.Errors);
        Assert.Equal("TextTooLong", issue.Code);
        Assert.Null(issue.RawValue);
    }

    [Fact]
    public async Task LegacyProducts_PreserveUnits_AndDoNotGuessUnitsForNewProducts()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Product.SetUnitCode("dozen");
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Apply(AdminImportTypes.Products,
            $"{ProductHeader}\nP-TEST,Producto Demo Editado,,true\nP-NEW,Producto Nuevo,,true");
        Assert.Empty(result.Errors);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal("dozen", (await fixture.Db.Products.SingleAsync(x => x.ExternalCode == "P-TEST")).UnitCode);
        Assert.Null((await fixture.Db.Products.SingleAsync(x => x.ExternalCode == "P-NEW")).UnitCode);
        var rejected = await fixture.Apply(AdminImportTypes.Products,
            $"{ProductHeader},unitCode\nP-TEST,Producto Demo,,true,liter");
        Assert.Contains(rejected.Errors, x => x.Code == "InvalidCode");
        Assert.Equal(0, rejected.UpdatedCount);
    }

    [Fact]
    public async Task LegacyFrequentImport_PreservesExplicitPositionsAndMachines_ButRevokesApproval()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Customer.RecordOrderingProfileChange(true, fixture.Now);
        fixture.Db.CustomerFrequentProducts.Add(new CustomerFrequentProduct
        {
            CustomerId = fixture.Customer.Id, ProductId = fixture.Product.Id,
            PreferredMachineId = fixture.Machine.Id, InterpretationPosition = 1, SortOrder = 9
        });
        await fixture.Db.SaveChangesAsync();
        var previousVersion = fixture.Customer.OrderingProfileVersion;
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Apply(AdminImportTypes.CustomerFrequentProducts,
            $"{FrequentHeader}\nC-TEST,,P-TEST,,5,2,true");
        Assert.Empty(result.Errors);
        fixture.Db.ChangeTracker.Clear();
        var customer = await fixture.Db.Customers.SingleAsync();
        var preference = await fixture.Db.CustomerFrequentProducts.SingleAsync();
        Assert.Equal(fixture.Machine.Id, preference.PreferredMachineId);
        Assert.Equal(1, preference.InterpretationPosition);
        Assert.Equal(2, preference.SortOrder);
        Assert.Equal(previousVersion + 1, customer.OrderingProfileVersion);
        Assert.False(customer.IsOrderProfileApproved);
    }

    [Fact]
    public async Task FrequentValidation_BlocksDuplicateInterpretationPositionsAndInactiveMachines()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.Products.Add(new Product { Id = Guid.NewGuid(), ExternalCode = "P-TWO", Name = "Segundo Demo", UnitCode = "kg" });
        await fixture.Db.SaveChangesAsync();
        var duplicate = await fixture.Validate(AdminImportTypes.CustomerFrequentProducts,
            $"{FrequentHeader},interpretationPosition\nC-TEST,,P-TEST,,5,1,true,1\nC-TEST,,P-TWO,,3,2,true,1");
        Assert.Contains(duplicate.Errors, x => x.Code == "DuplicateInterpretationPosition");
        fixture.Machine.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        var inactive = await fixture.Apply(AdminImportTypes.CustomerFrequentProducts,
            $"{FrequentHeader},preferredMachineExternalCode,interpretationPosition\nC-TEST,,P-TEST,,5,1,false,M-TEST,1");
        Assert.Contains(inactive.Errors, x => x.Code == "InactivePreferredMachine");
        Assert.Empty(await fixture.Db.CustomerFrequentProducts.ToArrayAsync());
    }

    [Fact]
    public async Task NewFrequentColumns_AreOptional_AndVisualOrderDoesNotCreateInterpretationPosition()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Apply(AdminImportTypes.CustomerFrequentProducts,
            $"{FrequentHeader}\nC-TEST,,P-TEST,,5,1,true");
        Assert.Empty(result.Errors);
        fixture.Db.ChangeTracker.Clear();
        var preference = await fixture.Db.CustomerFrequentProducts.SingleAsync();
        Assert.Null(preference.InterpretationPosition);
        Assert.Null(preference.PreferredMachineId);
        Assert.False((await fixture.Db.Customers.SingleAsync()).IsOrderProfileApproved);
    }

    [Fact]
    public async Task ReferenceMachines_AreUsedOnlyForPreview_ApplyRechecksPersistedCatalog()
    {
        await using var fixture = await Fixture.CreateAsync();
        var csv = $"{FrequentHeader},preferredMachineExternalCode,interpretationPosition\nC-TEST,,P-TEST,,5,1,true,M-REFERENCE,1";
        var request = new ImportCsvRequest(csv, "frequent.csv", new Dictionary<string, string>
        {
            [AdminImportTypes.Machines] = "externalCode,number,name,isActive\nM-REFERENCE,99,Maquina Referencia,true"
        });
        var preview = await fixture.Import.ValidateAsync(AdminImportTypes.CustomerFrequentProducts, request, CancellationToken.None);
        Assert.Empty(preview.Errors);
        var apply = await fixture.Import.ApplyAsync(AdminImportTypes.CustomerFrequentProducts, request, null, CancellationToken.None);
        Assert.Contains(apply.Errors, x => x.Code == "PreferredMachineNotFound");
        Assert.Empty(await fixture.Db.CustomerFrequentProducts.ToArrayAsync());
    }

    [Fact]
    public async Task ExplicitUnitMachineAndPosition_ArePersisted_WithoutApprovingTheProfile()
    {
        await using var fixture = await Fixture.CreateAsync();
        var productResult = await fixture.Apply(AdminImportTypes.Products,
            $"{ProductHeader},unitCode\nP-TEST,Producto Demo,,true,package");
        Assert.Empty(productResult.Errors);
        var frequentResult = await fixture.Apply(AdminImportTypes.CustomerFrequentProducts,
            $"{FrequentHeader},preferredMachineNumber,interpretationPosition\nC-TEST,,P-TEST,,3,8,true,1,2");
        Assert.Empty(frequentResult.Errors);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal("package", (await fixture.Db.Products.SingleAsync()).UnitCode);
        var preference = await fixture.Db.CustomerFrequentProducts.SingleAsync();
        Assert.Equal(fixture.Machine.Id, preference.PreferredMachineId);
        Assert.Equal(2, preference.InterpretationPosition);
        Assert.False((await fixture.Db.Customers.SingleAsync()).IsOrderProfileApproved);
    }

    [Theory]
    [InlineData("Producto Renombrado", "kg", true, true)]
    [InlineData("Producto Demo", "package", true, true)]
    [InlineData("Producto Demo", "kg", false, true)]
    [InlineData("Producto Demo", "kg", true, false)]
    public async Task ProductImport_InvalidatesReferencingProfilesOnlyWhenMeaningOrAvailabilityChanges(
        string name, string unitCode, bool isActive, bool expectedInvalidation)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Product.SetUnitCode("kg");
        fixture.Customer.RecordOrderingProfileChange(true, fixture.Now);
        fixture.Db.CustomerFrequentProducts.Add(new CustomerFrequentProduct
        {
            CustomerId = fixture.Customer.Id, ProductId = fixture.Product.Id,
            InterpretationPosition = 1, SortOrder = 1
        });
        await fixture.Db.SaveChangesAsync();
        var previousVersion = fixture.Customer.OrderingProfileVersion;
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Apply(AdminImportTypes.Products,
            $"{ProductHeader},unitCode\nP-TEST,{name},Descripcion actualizada,{isActive.ToString().ToLowerInvariant()},{unitCode}");

        Assert.Empty(result.Errors);
        fixture.Db.ChangeTracker.Clear();
        var customer = await fixture.Db.Customers.SingleAsync();
        Assert.Equal(!expectedInvalidation, customer.IsOrderProfileApproved);
        Assert.Equal(previousVersion + (expectedInvalidation ? 1 : 0), customer.OrderingProfileVersion);
        Assert.Equal(expectedInvalidation, await fixture.Db.AuditLogs.AnyAsync(x => x.EventType == "OrderingProfileInvalidated"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private Fixture(SqliteConnection connection, PedidosDbContext db, AdminImportService import,
            Customer customer, Product product, Machine machine, DateTimeOffset now)
        {
            this.connection = connection; Db = db; Import = import;
            Customer = customer; Product = product; Machine = machine; Now = now;
        }
        public PedidosDbContext Db { get; }
        public AdminImportService Import { get; }
        public Customer Customer { get; }
        public Product Product { get; }
        public Machine Machine { get; }
        public DateTimeOffset Now { get; }
        public Task<ImportApplyResponse> Apply(string type, string csv) => Import.ApplyAsync(type, new ImportCsvRequest(csv, "test.csv"), null, CancellationToken.None);
        public Task<ImportValidationResponse> Validate(string type, string csv) => Import.ValidateAsync(type, new ImportCsvRequest(csv, "test.csv"), CancellationToken.None);
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var now = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.FromHours(-6));
            var customer = Customer.Create("Cliente Demo", null, null, null, null, null, now);
            customer.SetExternalCode("C-TEST", now);
            var product = Product.Create("Producto Demo", null);
            product.SetExternalCode("P-TEST");
            var machine = new Machine { Id = Guid.NewGuid(), ExternalCode = "M-TEST", Number = 1, Name = "Maquina Demo" };
            db.AddRange(customer, product, machine);
            await db.SaveChangesAsync();
            var import = new AdminImportService(new CsvImportParser(), new EfCustomerRepository(db), new EfProductRepository(db),
                new EfMachineRepository(db), new EfAuditLogRepository(db), new Clock(now));
            return new Fixture(connection, db, import, customer, product, machine, now);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class Clock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset Now => now;
        public DateOnly Today => DateOnly.FromDateTime(now.DateTime);
        public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(now.DateTime);
    }
}
