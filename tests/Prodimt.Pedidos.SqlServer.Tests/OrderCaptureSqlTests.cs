using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class OrderCaptureSqlTests(SqlServerFixture database)
{
    private static readonly DateOnly Day = new(2026,10,25);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private static string Url(Guid customer) => $"/api/admin/customers/{customer}/orders";

    [Fact]
    public async Task PreviewAndCapture_ResolveCustomerAliasesAndUnits_AndReplayAcrossHosts()
    {
        var catalog=await database.CreateCatalogAsync();
        SubmitOrderCaptureRequest request;
        Guid orderId;
        await using(var factory=database.CreateFactory())
        using(var client=await SqlServerFixture.CreateAdminClientAsync(factory))
        {
            await Configure(client,catalog);
            var preview=await Preview(client,catalog.CustomerId,"13+2hn");
            Assert.Equal(13,preview.Lines.Single(x=>x.ProductId==catalog.ProductId).Quantity);
            Assert.Equal("package",preview.Lines.Single(x=>x.ProductId==catalog.SecondProductId).UnitCode);
            request=new(Guid.NewGuid(),preview.Profile.OrderingProfileVersion,Day,preview.Text,preview.Fingerprint);
            using var response=await client.PostAsJsonAsync(Url(catalog.CustomerId)+"/capture",request);
            response.EnsureSuccessStatusCode();
            var saved=(await response.Content.ReadFromJsonAsync<SubmitOrderCaptureResponse>(JsonOptions))!;
            orderId=saved.Order.OrderId;Assert.False(saved.Replayed);
        }
        await using var next=database.CreateFactory();
        using var nextClient=await SqlServerFixture.CreateAdminClientAsync(next);
        using var replay=await nextClient.PostAsJsonAsync(Url(catalog.CustomerId)+"/capture",request);
        replay.EnsureSuccessStatusCode();
        var repeated=(await replay.Content.ReadFromJsonAsync<SubmitOrderCaptureResponse>(JsonOptions))!;
        Assert.True(repeated.Replayed);Assert.Equal(orderId,repeated.Order.OrderId);
        using var changed=await nextClient.PostAsJsonAsync(Url(catalog.CustomerId)+"/capture",request with { Text="14+2HN" });
        Assert.Equal(HttpStatusCode.Conflict,changed.StatusCode);
        using var duplicate=await nextClient.PostAsJsonAsync(Url(catalog.CustomerId)+"/capture",request with { OperationId=Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);
        await using var db=database.CreateDbContext();
        var order=await db.Orders.Include(x=>x.Lines).SingleAsync(x=>x.CustomerId==catalog.CustomerId);
        Assert.Equal(new TimeOnly(12,30),order.RequestedDeliveryTime);
        Assert.Equal(2,order.Lines.Count);
        var audit=await db.OrderAuditLogs.SingleAsync(x=>x.OrderId==orderId&&x.EventType==OrderAuditEventType.AdminOrderCaptureResolved);
        Assert.Contains("13+2hn",audit.MetadataJson);
        var key=$"CAPTURE:{catalog.CustomerId:N}:{request.OperationId:N}";
        Assert.Single(await db.OrderOperations.Where(x=>x.OperationKey==key).ToArrayAsync());
        Assert.Equal("HN",(await db.CustomerFrequentProducts.SingleAsync(x=>x.CustomerId==catalog.CustomerId&&x.ProductId==catalog.SecondProductId)).CaptureAlias);
    }

    [Fact]
    public async Task UnitChangedAfterPreview_ReturnsConflict_WithoutOrderOrReceipt()
    {
        var catalog=await database.CreateCatalogAsync();
        await using var factory=database.CreateFactory();
        using var client=await SqlServerFixture.CreateAdminClientAsync(factory);
        await Configure(client,catalog);
        var preview=await Preview(client,catalog.CustomerId,"16");
        using var edit=await client.PutAsJsonAsync($"/api/admin/products/{catalog.ProductId}",
            new UpsertAdminProductRequest(preview.Lines.Single().ProductName,null,"piece"));
        edit.EnsureSuccessStatusCode();
        var request=new SubmitOrderCaptureRequest(Guid.NewGuid(),preview.Profile.OrderingProfileVersion,Day,preview.Text,preview.Fingerprint);
        using var response=await client.PostAsJsonAsync(Url(catalog.CustomerId)+"/capture",request);
        Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        await using var db=database.CreateDbContext();
        Assert.False(await db.Orders.AnyAsync(x=>x.CustomerId==catalog.CustomerId));
        var key=$"CAPTURE:{catalog.CustomerId:N}:{request.OperationId:N}";
        Assert.False(await db.OrderOperations.AnyAsync(x=>x.OperationKey==key));
    }

    [Fact]
    public async Task TwoConcurrentCaptures_DoNotCreateTwoDeliveriesWithoutExplicitConfirmation()
    {
        var catalog=await database.CreateCatalogAsync();
        await using var factory=database.CreateFactory();
        using var first=await SqlServerFixture.CreateAdminClientAsync(factory);
        using var second=await SqlServerFixture.CreateAdminClientAsync(factory);
        await Configure(first,catalog);
        var preview=await Preview(first,catalog.CustomerId,"16");
        SubmitOrderCaptureRequest Request()=>new(Guid.NewGuid(),preview.Profile.OrderingProfileVersion,Day,preview.Text,preview.Fingerprint);
        var responses=await Task.WhenAll(first.PostAsJsonAsync(Url(catalog.CustomerId)+"/capture",Request()),
            second.PostAsJsonAsync(Url(catalog.CustomerId)+"/capture",Request()));
        try
        {
            Assert.Single(responses,x=>x.IsSuccessStatusCode);
            Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        }
        finally { foreach(var response in responses)response.Dispose(); }
        await using var db=database.CreateDbContext();
        Assert.Single(await db.Orders.Where(x=>x.CustomerId==catalog.CustomerId).ToArrayAsync());
        var prefix=$"CAPTURE:{catalog.CustomerId:N}:";
        Assert.Single(await db.OrderOperations.Where(x=>x.OperationKey.StartsWith(prefix)).ToArrayAsync());
    }

    [Fact]
    public async Task AuditFailure_RollsBackCaptureAndReceipt_ThenSameReferenceCanRetry()
    {
        var catalog=await database.CreateCatalogAsync();
        await using var factory=database.CreateFactory();
        using var client=await SqlServerFixture.CreateAdminClientAsync(factory);
        await Configure(client,catalog);
        var preview=await Preview(client,catalog.CustomerId,"16");
        var request=new SubmitOrderCaptureRequest(Guid.NewGuid(),preview.Profile.OrderingProfileVersion,Day,preview.Text,preview.Fingerprint);
        var constraint="CK_CaptureTest_"+Guid.NewGuid().ToString("N");
        await Ddl($"ALTER TABLE [OrderAuditLogs] WITH NOCHECK ADD CONSTRAINT [{constraint}] CHECK ([EventType] <> N'AdminOrderCaptureResolved')");
        try
        {
            HttpResponseMessage? response=null;Exception? failure=null;
            try { response=await client.PostAsJsonAsync(Url(catalog.CustomerId)+"/capture",request); }
            catch(Exception ex){failure=ex;}
            using(response) Assert.True(failure is not null || response?.StatusCode==HttpStatusCode.InternalServerError);
            await using var db=database.CreateDbContext();
            Assert.False(await db.Orders.AnyAsync(x=>x.CustomerId==catalog.CustomerId));
            var key=$"CAPTURE:{catalog.CustomerId:N}:{request.OperationId:N}";
            Assert.False(await db.OrderOperations.AnyAsync(x=>x.OperationKey==key));
        }
        finally { await Ddl($"ALTER TABLE [OrderAuditLogs] DROP CONSTRAINT [{constraint}]"); }
        using var retry=await client.PostAsJsonAsync(Url(catalog.CustomerId)+"/capture",request);
        retry.EnsureSuccessStatusCode();
        await using var persisted=database.CreateDbContext();
        var order=await persisted.Orders.SingleAsync(x=>x.CustomerId==catalog.CustomerId);
        Assert.Single(await persisted.OrderAuditLogs.Where(x=>x.OrderId==order.Id&&x.EventType==OrderAuditEventType.AdminOrderCaptureResolved).ToArrayAsync());
    }

    private static async Task Configure(HttpClient client,SqlServerFixture.TestCatalog catalog)
    {
        var profile=new UpdateCustomerFrequentProductsRequest([
            new(catalog.ProductId,null,2,true,InterpretationPosition:1,ExpectedUnitCode:"kg"),
            new(catalog.SecondProductId,null,1,true,InterpretationPosition:2,ExpectedUnitCode:"package",CaptureAlias:"hn")],true,0);
        using var response=await client.PutAsJsonAsync($"/api/admin/customers/{catalog.CustomerId}/frequent-products",profile);
        response.EnsureSuccessStatusCode();
        using var edit=await client.PutAsJsonAsync($"/api/admin/customers/{catalog.CustomerId}",new UpsertAdminCustomerRequest(
            "Synthetic capture "+catalog.CustomerCode,null,new TimeOnly(12,30),null,null,null));
        edit.EnsureSuccessStatusCode();
    }
    private static async Task<PreviewOrderCaptureResponse> Preview(HttpClient client,Guid customer,string text)
    {
        using var response=await client.PostAsJsonAsync(Url(customer)+"/preview",new PreviewOrderCaptureRequest(text,Day));
        response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<PreviewOrderCaptureResponse>())!;
    }
    private async Task Ddl(string sql)
    {
        await using var db=database.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
