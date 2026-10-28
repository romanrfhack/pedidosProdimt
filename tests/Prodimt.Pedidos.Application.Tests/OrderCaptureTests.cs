using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminImports;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class OrderCaptureTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly AdminActorContext Actor = new("synthetic-admin", "Synthetic administrator");
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), Extra = Guid.NewGuid();
    private static CustomerOrderingProfileSnapshot Profile(bool approved = true) => new(Guid.NewGuid(), 3, approved,
        [new(A, "Producto #11", null, 3, true, null, 1, "kg", true),
         new(B, "Producto #14", null, 1, true, null, 2, "kg", true),
         new(Extra, "Producto especial", null, 2, true, null, 3, "kg", true, "HN")]);

    [Theory]
    [InlineData("13+7", 13, 7, 0)]
    [InlineData("16", 16, 0, 0)]
    [InlineData("1+5+2HN", 1, 5, 2)]
    [InlineData(" 1 + 5 + 2hn ", 1, 5, 2)]
    [InlineData("1,5+2.25", 1.5, 2.25, 0)]
    [InlineData("0+7", 0, 7, 0)]
    [InlineData("2HN+5", 5, 0, 2)]
    public void Parse_UsesExplicitCustomerPositionsAndAliases_NotVisualOrder(string text, double a, double b, double extra)
    {
        var result = OrderCaptureParser.Parse(text, Profile());
        decimal Qty(Guid id) => result.SingleOrDefault(x => x.ProductId == id)?.Quantity ?? 0;
        Assert.Equal((decimal)a, Qty(A)); Assert.Equal((decimal)b, Qty(B)); Assert.Equal((decimal)extra, Qty(Extra));
        Assert.All(result, x => Assert.Equal("kg", x.UnitCode));
    }

    [Theory]
    [InlineData("")][InlineData("13++7")][InlineData("+13")][InlineData("13+")][InlineData("-1")]
    [InlineData("0")][InlineData("X")][InlineData("13/7")][InlineData("1.234")][InlineData("mañana 16")]
    [InlineData("2DESCONOCIDA")][InlineData("1+2+3+4")][InlineData("1+2+3+2HN")]
    [InlineData("9999999999999999999999999999999999999")]
    public void Parse_RejectsAmbiguousOrInvalidInput(string text) => Assert.Throws<ArgumentException>(() => OrderCaptureParser.Parse(text, Profile()));

    [Fact]
    public void Parse_UsesTheSelectedCustomerAndRequiresConfirmedProductMeaning()
    {
        var other = Profile() with { Items = [new(B, "Producto #12", null, 1, true, null, 1, "piece", true)] };
        var line = Assert.Single(OrderCaptureParser.Parse("16", other));
        Assert.Equal(B, line.ProductId); Assert.Equal("piece", line.UnitCode);
        Assert.Throws<ArgumentException>(() => OrderCaptureParser.Parse("16", other with { Items = [other.Items[0] with { UnitCode = null }] }));
        Assert.Throws<ArgumentException>(() => OrderCaptureParser.Parse("16", other with { Items = [other.Items[0] with { ProductIsActive = false }] }));
        Assert.Throws<ArgumentException>(() => OrderCaptureParser.Parse("16", other with { Items = [other.Items[0] with { InterpretationPosition = 2 }] }));
    }

    [Theory]
    [InlineData(" hn ","HN")][InlineData("",null)][InlineData(null,null)][InlineData("ABCDE","ABCDE")]
    public void Alias_NormalizesLettersOnly(string? input, string? expected) => Assert.Equal(expected, OrderCaptureAlias.Normalize(input));
    [Theory]
    [InlineData("H N")][InlineData("HN2")][InlineData("H+N")][InlineData("HARINA-N")][InlineData("ABCDEFGHIJKLMNOQP")]
    public void Alias_RejectsInvalid(string value) => Assert.Throws<ArgumentException>(() => OrderCaptureAlias.Normalize(value));

    [Theory]
    [InlineData("preview")][InlineData("capture")]
    public async Task Endpoints_RequireAdmin(string endpoint)
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        var path = $"/api/admin/customers/{Guid.NewGuid()}/orders/{endpoint}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, new {})).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/customer-token", new { token = DevelopmentAuthSeedValues.DefaultCustomerToken });
        var customer = (await login.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new {})).StatusCode);
    }

    [Fact]
    public async Task Preview_DoesNotWriteOrApprove_AndSaveRequiresExplicitApproval()
    {
        await using var f = await Fixture.Create(false);
        var preview = await f.Service.PreviewAsync(f.Customer.Id, new("13+7", f.Day), Ct);
        Assert.False(preview.Profile.IsOrderProfileApproved);
        Assert.Empty(await f.Db.Orders.ToArrayAsync()); Assert.Empty(await f.Db.OrderOperations.ToArrayAsync());
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Service.SubmitAsync(f.Customer.Id, f.Request(preview), Actor, Ct));
        Assert.Empty(await f.Db.Orders.ToArrayAsync()); Assert.Empty(await f.Db.OrderAuditLogs.ToArrayAsync());
    }

    [Fact]
    public async Task Save_IsAtomicIdempotentAuditedAndInheritsClientDelivery()
    {
        await using var f = await Fixture.Create();
        var preview = await f.Service.PreviewAsync(f.Customer.Id, new("1+5+2HN", f.Day), Ct);
        var request = f.Request(preview);
        var first = await f.Service.SubmitAsync(f.Customer.Id, request, Actor, Ct);
        var replay = await f.Service.SubmitAsync(f.Customer.Id, request, Actor, Ct);
        Assert.False(first.Replayed); Assert.True(replay.Replayed); Assert.Equal(first.Order.OrderId, replay.Order.OrderId);
        f.Db.ChangeTracker.Clear();
        var order = await f.Db.Orders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(f.Day, order.OrderDate); Assert.Equal(new TimeOnly(12,30), order.RequestedDeliveryTime);
        Assert.Equal(3, order.Lines.Count); Assert.Equal(8, order.Lines.Sum(x => x.Quantity));
        Assert.Equal(1, await f.Db.OrderOperations.CountAsync());
        var audit = await f.Db.OrderAuditLogs.SingleAsync(x => x.EventType == OrderAuditEventType.AdminOrderCaptureResolved);
        using var json = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("1+5+2HN", json.RootElement.GetProperty("originalText").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("lines").GetArrayLength());
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.SubmitAsync(f.Customer.Id,
            request with { InternalNotes = "contenido distinto" }, Actor, Ct));
        Assert.Equal(1, await f.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task Save_RejectsChangedTextProfileUnitAndDuplicateWithoutWriting()
    {
        await using var f = await Fixture.Create();
        var preview = await f.Service.PreviewAsync(f.Customer.Id, new("16", f.Day), Ct);
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Service.SubmitAsync(f.Customer.Id,
            f.Request(preview) with { Text = "17" }, Actor, Ct));
        var tracked = await f.Db.Customers.SingleAsync(); tracked.RecordOrderingProfileChange(true, new Clock().Now);
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Service.SubmitAsync(f.Customer.Id, f.Request(preview), Actor, Ct));
        var current = await f.Service.PreviewAsync(f.Customer.Id, new("16", f.Day), Ct);
        var product = await f.Db.Products.SingleAsync(x => x.Id == f.Products[0].Id); product.SetUnitCode("piece");
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Service.SubmitAsync(f.Customer.Id, f.Request(current), Actor, Ct));
        Assert.Empty(await f.Db.Orders.ToArrayAsync()); Assert.Empty(await f.Db.OrderOperations.ToArrayAsync());
        current = await f.Service.PreviewAsync(f.Customer.Id, new("16", f.Day), Ct);
        await f.Service.SubmitAsync(f.Customer.Id, f.Request(current), Actor, Ct);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.SubmitAsync(f.Customer.Id, f.Request(current), Actor, Ct));
        Assert.Equal(1, await f.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task Manual_CanUseUnapprovedProfileButProtectsUnitAndModes()
    {
        await using var f = await Fixture.Create(false);
        var request = new SubmitOrderCaptureRequest(Guid.NewGuid(), 1, f.Day,
            Lines:[new(f.Products[0].Id, 12, "kg")]);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SubmitAsync(f.Customer.Id, request with { Text = "12" }, Actor, Ct));
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Service.SubmitAsync(f.Customer.Id,
            request with { Lines = [new(f.Products[0].Id, 12, "package")] }, Actor, Ct));
        var saved = await f.Service.SubmitAsync(f.Customer.Id, request, Actor, Ct);
        Assert.Equal(new TimeOnly(12,30), saved.Order.RequestedDeliveryTime);
        Assert.False((await f.Db.Customers.AsNoTracking().SingleAsync()).IsOrderProfileApproved);
    }

    [Fact]
    public async Task AuditFailure_RollsBackOrderAndReceipt()
    {
        await using var f = await Fixture.Create();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_capture_audit BEFORE INSERT ON OrderAuditLogs WHEN NEW.EventType='AdminOrderCaptureResolved' BEGIN SELECT RAISE(ABORT,'fictional failure'); END;");
        var preview = await f.Service.PreviewAsync(f.Customer.Id, new("16", f.Day), Ct);
        await Assert.ThrowsAnyAsync<Exception>(() => f.Service.SubmitAsync(f.Customer.Id, f.Request(preview), Actor, Ct));
        Assert.Empty(await f.Db.Orders.ToArrayAsync()); Assert.Empty(await f.Db.OrderOperations.ToArrayAsync()); Assert.Empty(await f.Db.OrderAuditLogs.ToArrayAsync());
    }

    [Fact]
    public async Task LegacyProfilePut_PreservesAlias_ExplicitEmptyClears_AndDuplicateAliasesFail()
    {
        await using var f = await Fixture.Create();
        var catalog = new AdminCustomerCatalogService(new EfCustomerRepository(f.Db),new EfProductRepository(f.Db),
            new EfMachineRepository(f.Db),new EfAuditLogRepository(f.Db),new Clock());
        UpdateCustomerFrequentProductsRequest Request(int version, string? alias) => new(f.Products.Select((p,i) =>
            new UpdateCustomerFrequentProductItemRequest(p.Id,null,i+1,true,InterpretationPosition:i+1,
                CaptureAlias:i==2?alias:null)).ToArray(),ExpectedProfileVersion:version);
        await catalog.ReplaceFrequentProductsAsync(f.Customer.Id,Request(1,null),Actor,Ct);
        Assert.Equal("HN",(await f.Db.CustomerFrequentProducts.AsNoTracking().SingleAsync(x=>x.ProductId==f.Products[2].Id)).CaptureAlias);
        var repeated = Request(2,"HN") with { Items = f.Products.Select((p,i) =>
            new UpdateCustomerFrequentProductItemRequest(p.Id,null,i+1,true,InterpretationPosition:i+1,CaptureAlias:"hn")).ToArray() };
        await Assert.ThrowsAsync<ArgumentException>(()=>catalog.ReplaceFrequentProductsAsync(f.Customer.Id,repeated,Actor,Ct));
        await catalog.ReplaceFrequentProductsAsync(f.Customer.Id,Request(2,""),Actor,Ct);
        Assert.All(await f.Db.CustomerFrequentProducts.AsNoTracking().ToArrayAsync(),x=>Assert.Null(x.CaptureAlias));
    }

    [Fact]
    public async Task LegacyCsv_PreservesAlias_ExplicitEmptyClears_AndDoesNotApprove()
    {
        await using var f=await Fixture.Create();
        var importer=new AdminImportService(new CsvImportParser(),new EfCustomerRepository(f.Db),new EfProductRepository(f.Db),
            new EfMachineRepository(f.Db),new EfAuditLogRepository(f.Db),new Clock());
        const string header="customerExternalCode,customerName,productExternalCode,productName,defaultQuantity,sortOrder,isActive";
        string Rows(string tail="")=>string.Join('\n',f.Products.Select((p,i)=>$",{f.Customer.Name},,{p.Name},,{i+1},true{tail}"));
        var old=await importer.ApplyAsync(AdminImportTypes.CustomerFrequentProducts,new(header+"\n"+Rows(),"fictional.csv"),Actor,Ct);
        Assert.Equal(1,old.UpdatedCount);Assert.Empty(old.Errors);
        Assert.Equal("HN",(await f.Db.CustomerFrequentProducts.AsNoTracking().SingleAsync(x=>x.ProductId==f.Products[2].Id)).CaptureAlias);
        Assert.False((await f.Db.Customers.AsNoTracking().SingleAsync()).IsOrderProfileApproved);
        var duplicate=await importer.ValidateAsync(AdminImportTypes.CustomerFrequentProducts,new(header+",captureAlias\n"+Rows(",hn"),"fictional.csv"),Ct);
        Assert.Contains(duplicate.Errors,x=>x.Code=="DuplicateCaptureAlias");
        var clear=await importer.ApplyAsync(AdminImportTypes.CustomerFrequentProducts,new(header+",captureAlias\n"+Rows(","),"fictional.csv"),Actor,Ct);
        Assert.Empty(clear.Errors);Assert.Equal(1,clear.UpdatedCount);
        Assert.All(await f.Db.CustomerFrequentProducts.AsNoTracking().ToArrayAsync(),x=>Assert.Null(x.CaptureAlias));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public PedidosDbContext Db { get; }
        public Customer Customer { get; }
        public Product[] Products { get; }
        public AdminOrderCaptureService Service { get; }
        public DateOnly Day => new Clock().Today.AddDays(1);
        private Fixture(SqliteConnection c, PedidosDbContext db, Customer customer, Product[] products)
        {
            connection=c; Db=db; Customer=customer; Products=products;
            var customers=new EfCustomerRepository(db); var orders=new EfOrderRepository(db); var audit=new EfOrderAuditLogRepository(db);
            var machines=new EfMachineRepository(db); var publisher=new OrderChangeNoticePublisher(new EfOrderChangeNoticeRepository(db),machines,customers);
            var service=new AdminOrderService(orders,audit,customers,new EfProductRepository(db),machines,new EfSalesChannelRepository(db),new Clock(),publisher);
            Service=new(customers,orders,audit,new EfOrderOperationStore(db),new Clock(),service);
        }
        public SubmitOrderCaptureRequest Request(PreviewOrderCaptureResponse p) => new(Guid.NewGuid(),p.Profile.OrderingProfileVersion,Day,p.Text,p.Fingerprint);
        public static async Task<Fixture> Create(bool approved=true)
        {
            var c=new SqliteConnection("Data Source=:memory:");await c.OpenAsync();
            var db=new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(c).Options);
            await db.Database.EnsureCreatedAsync();
            var customer=Customer.Create("Cliente ficticio captura",null,new TimeOnly(12,30),null,null,null,new Clock().Now);
            customer.RecordOrderingProfileChange(approved,new Clock().Now);
            var products=new[]{Product.Create("Variante #11",null),Product.Create("Variante #14",null),Product.Create("Especial ficticio",null)};
            foreach(var p in products)p.SetUnitCode("kg");
            db.AddRange(products);db.Add(customer);
            db.Add(new SalesChannel{Id=Guid.NewGuid(),Name="Captura",Type=SalesChannelType.AdminManualCapture,IsInternal=true});
            for(var i=0;i<products.Length;i++)db.Add(new CustomerFrequentProduct{CustomerId=customer.Id,ProductId=products[i].Id,
                SortOrder=3-i,InterpretationPosition=i+1,CaptureAlias=i==2?"HN":null});
            await db.SaveChangesAsync();db.ChangeTracker.Clear();return new(c,db,customer,products);
        }
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await connection.DisposeAsync();}
    }
    private sealed class Clock:IDateTimeProvider
    { public DateTimeOffset Now=>new(2026,10,28,14,0,0,TimeSpan.Zero);public DateOnly Today=>new(2026,10,28);public TimeOnly LocalTimeOfDay=>new(8,0); }
}
