using System.Text.Json.Serialization;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<CustomerOrderService>();
builder.Services.AddScoped<AdminOrderService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

var customerOrders = app.MapGroup("/api/customer-orders")
    .WithTags("Customer orders");

customerOrders.MapGet("/{customerId:guid}/today", async (
    Guid customerId,
    CustomerOrderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetTodayAsync(customerId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("GetCustomerOrderToday");

customerOrders.MapPost("/{customerId:guid}/submit", async (
    Guid customerId,
    SubmitCustomerOrderRequest request,
    CustomerOrderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.SubmitAsync(customerId, request, cancellationToken));
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("SubmitCustomerOrder");

customerOrders.MapPost("/{customerId:guid}/no-order", async (
    Guid customerId,
    CustomerOrderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.MarkNoOrderAsync(customerId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("MarkCustomerNoOrder");

var adminOrders = app.MapGroup("/api/admin/orders")
    .WithTags("Admin orders");

adminOrders.MapGet("/today", async (
    AdminOrderService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetTodayAsync(cancellationToken));
})
.WithName("GetAdminOrdersToday");

adminOrders.MapGet("/pending-review", async (
    AdminOrderService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetPendingReviewAsync(cancellationToken));
})
.WithName("GetAdminOrdersPendingReview");

adminOrders.MapPost("/{orderId:guid}/review", async (
    Guid orderId,
    ReviewOrderRequest request,
    AdminOrderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ReviewAsync(orderId, request, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("ReviewAdminOrder");

app.Run();
