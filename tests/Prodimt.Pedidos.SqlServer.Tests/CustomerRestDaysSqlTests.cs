using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class CustomerRestDaysSqlTests(SqlServerFixture database)
{
    [Fact]
    public async Task Schedule_PersistsAcrossHosts_AndExistingCsvPreservesIt()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var factory = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(factory);
        var url = "/api/admin/customers/" + catalog.CustomerId;
        var original = (await client.GetFromJsonAsync<AdminCustomerResponse>(url))!;
        Assert.Empty(original.RestDays!);
        var date = factory.Services.GetRequiredService<IDateTimeProvider>().Today;
        var weekday = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
        var update = new UpsertAdminCustomerRequest(original.Name, original.PhoneNumber, original.PreferredDeliveryTime,
            original.PreferredDeliveryWindowStart, original.PreferredDeliveryWindowEnd, original.DeliveryNotes,
            original.RouteCode, original.RouteStopOrder, original.HasStoreKey, original.PaymentTermCode,
            original.InternalDeliveryInstructions, [weekday]);
        (await client.PutAsJsonAsync(url, update)).EnsureSuccessStatusCode();

        await using var freshFactory = database.CreateFactory();
        using var freshClient = await SqlServerFixture.CreateAdminClientAsync(freshFactory);
        var persisted = (await freshClient.GetFromJsonAsync<AdminCustomerResponse>(url))!;
        Assert.Equal([weekday], persisted.RestDays);
        Assert.Equal(original.InternalDeliveryInstructions, persisted.InternalDeliveryInstructions);
        Assert.Equal(original.OrderingProfileVersion, persisted.OrderingProfileVersion);
        var csv = "externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes\n"
            + catalog.CustomerCode + "," + original.Name + ",,true,,,,";
        (await freshClient.PostAsJsonAsync("/api/admin/import/customers/apply", new { content = csv, fileName = "synthetic-rest-compat.csv" })).EnsureSuccessStatusCode();
        Assert.Equal([weekday], (await freshClient.GetFromJsonAsync<AdminCustomerResponse>(url))!.RestDays);
        await using var db = database.CreateDbContext();
        var before = await db.Orders.CountAsync();
        var day = await freshClient.GetFromJsonAsync<JsonElement>("/api/admin/customers/day-status");
        Assert.Equal(date.ToString("yyyy-MM-dd"), day.GetProperty("date").GetString());
        var status = day.GetProperty("customers").EnumerateArray().Single(row => row.GetProperty("customerId").GetGuid() == catalog.CustomerId);
        Assert.Equal("ScheduledRest", status.GetProperty("status").GetString());
        Assert.Equal(before, await db.Orders.CountAsync());
        var customer = await db.Customers.SingleAsync(row => row.Id == catalog.CustomerId);
        Assert.Equal(1 << (weekday - 1), customer.RestDaysMask);
        Assert.True(await db.AuditLogs.AnyAsync(row => row.EntityId == catalog.CustomerId.ToString()
            && row.EventType == "CustomerUpdated" && row.MetadataJson!.Contains("restDays")));
    }
}
