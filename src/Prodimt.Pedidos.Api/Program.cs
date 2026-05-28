using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Infrastructure;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);
var jwtSettings = JwtSettings.FromConfiguration(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<CustomerOrderService>();
builder.Services.AddScoped<AdminOrderService>();
builder.Services.AddScoped<PilotAuthenticationService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CustomerAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(ProdimtAuthClaims.ActorType, ProdimtActorTypes.Customer);
    });

    options.AddPolicy("AdminAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(ProdimtAuthClaims.ActorType, ProdimtActorTypes.Admin);
    });
});
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
app.UseAuthentication();
app.UseAuthorization();

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

var auth = app.MapGroup("/api/auth")
    .WithTags("Authentication");

auth.MapPost("/customer-token", async (
    CustomerTokenLoginRequest request,
    PilotAuthenticationService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.LoginCustomerWithTokenAsync(request, cancellationToken));
    }
    catch (AuthenticationFailedException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
    }
})
.WithName("LoginCustomerWithToken")
.AllowAnonymous();

auth.MapPost("/admin/login", async (
    AdminLoginRequest request,
    PilotAuthenticationService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.LoginAdminAsync(request, cancellationToken));
    }
    catch (AuthenticationFailedException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
    }
})
.WithName("LoginAdmin")
.AllowAnonymous();

var customerOrders = app.MapGroup("/api/customer-orders")
    .WithTags("Customer orders")
    .RequireAuthorization("CustomerAccess");

customerOrders.MapGet("/{customerId:guid}/today", async (
    Guid customerId,
    ClaimsPrincipal user,
    CustomerOrderService service,
    CancellationToken cancellationToken) =>
{
    var authorizationFailure = ValidateCustomerAccess(user, customerId);
    if (authorizationFailure is not null)
    {
        return authorizationFailure;
    }

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
    ClaimsPrincipal user,
    SubmitCustomerOrderRequest request,
    CustomerOrderService service,
    CancellationToken cancellationToken) =>
{
    var authorizationFailure = ValidateCustomerAccess(user, customerId);
    if (authorizationFailure is not null)
    {
        return authorizationFailure;
    }

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
    ClaimsPrincipal user,
    CustomerOrderService service,
    CancellationToken cancellationToken) =>
{
    var authorizationFailure = ValidateCustomerAccess(user, customerId);
    if (authorizationFailure is not null)
    {
        return authorizationFailure;
    }

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
    .WithTags("Admin orders")
    .RequireAuthorization("AdminAccess");

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

adminOrders.MapGet("/{orderId:guid}/audit", async (
    Guid orderId,
    AdminOrderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetAuditAsync(orderId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("GetAdminOrderAudit");

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

static IResult? ValidateCustomerAccess(ClaimsPrincipal user, Guid customerId)
{
    var customerIdClaim = user.FindFirstValue(ProdimtAuthClaims.CustomerId);

    if (!Guid.TryParse(customerIdClaim, out var authenticatedCustomerId) || authenticatedCustomerId != customerId)
    {
        return Results.Forbid();
    }

    return null;
}

public partial class Program;
