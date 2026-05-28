using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Infrastructure;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<CustomerOrderService>();
builder.Services.AddScoped<AdminOrderService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProdimtWeb", policy =>
    {
        policy.WithOrigins("http://127.0.0.1:4200", "http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    try
    {
        await app.Services.ApplyDevelopmentSeedAsync(builder.Configuration, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Development seed could not be applied. Verify SQL Server and migrations.");
    }
}

app.UseHttpsRedirection();
app.UseCors("ProdimtWeb");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.MapGet("/health/db", async (
    IServiceProvider serviceProvider,
    CancellationToken cancellationToken) =>
{
    using var scope = serviceProvider.CreateScope();
    var dbContext = scope.ServiceProvider.GetService<PedidosDbContext>();

    if (dbContext is null)
    {
        return Results.Problem(
            title: "Database persistence is not registered.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

        if (canConnect)
        {
            return Results.Ok(new { status = "ok", database = "reachable" });
        }

        return Results.Problem(
            title: "Database is not reachable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception)
    {
        return Results.Problem(
            title: "Database is not reachable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.WithName("DatabaseHealth");

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
    catch (ArgumentException ex)
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
    catch (CustomerOrderConflictException ex)
    {
        return Results.Conflict(new { error = ex.Message });
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
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("ReviewAdminOrder");

app.Run();
