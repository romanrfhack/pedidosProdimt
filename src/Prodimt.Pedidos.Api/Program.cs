using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminImports;
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
builder.Services.AddScoped<AdminCustomerCatalogService>();
builder.Services.AddScoped<AdminProductCatalogService>();
builder.Services.AddScoped<AdminMachineCatalogService>();
builder.Services.AddScoped<AdminCustomerAccessTokenService>();
builder.Services.AddScoped<AdminUserCatalogService>();
builder.Services.AddScoped<CsvImportParser>();
builder.Services.AddScoped<AdminImportService>();
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

adminOrders.MapGet("/{orderId:guid}", async (
    Guid orderId,
    AdminOrderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetDetailAsync(orderId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("GetAdminOrderDetail");

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
    ClaimsPrincipal user,
    AdminOrderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ReviewAsync(orderId, request, cancellationToken, GetAdminActor(user)));
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

var adminCustomers = app.MapGroup("/api/admin/customers")
    .WithTags("Admin customers")
    .RequireAuthorization("AdminAccess");

adminCustomers.MapGet("", async (
    AdminCustomerCatalogService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetAllAsync(cancellationToken));
})
.WithName("GetAdminCustomers");

adminCustomers.MapGet("/{customerId:guid}", async (
    Guid customerId,
    AdminCustomerCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetByIdAsync(customerId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("GetAdminCustomer");

adminCustomers.MapPost("", async (
    UpsertAdminCustomerRequest request,
    ClaimsPrincipal user,
    AdminCustomerCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await service.CreateAsync(request, GetAdminActor(user), cancellationToken);
        return Results.Created($"/api/admin/customers/{response.Id}", response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("CreateAdminCustomer");

adminCustomers.MapPut("/{customerId:guid}", async (
    Guid customerId,
    UpsertAdminCustomerRequest request,
    ClaimsPrincipal user,
    AdminCustomerCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.UpdateAsync(customerId, request, GetAdminActor(user), cancellationToken));
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
.WithName("UpdateAdminCustomer");

adminCustomers.MapPatch("/{customerId:guid}/activate", async (
    Guid customerId,
    ClaimsPrincipal user,
    AdminCustomerCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ActivateAsync(customerId, GetAdminActor(user), cancellationToken));
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
.WithName("ActivateAdminCustomer");

adminCustomers.MapPatch("/{customerId:guid}/deactivate", async (
    Guid customerId,
    ClaimsPrincipal user,
    AdminCustomerCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.DeactivateAsync(customerId, GetAdminActor(user), cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("DeactivateAdminCustomer");

adminCustomers.MapGet("/pending-orders", async (
    DateOnly? date,
    AdminOrderService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetPendingCustomersAsync(date, cancellationToken));
})
.WithName("GetAdminCustomersPendingOrders");

adminCustomers.MapGet("/{customerId:guid}/order-template", async (
    Guid customerId,
    AdminOrderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetOrderTemplateAsync(customerId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("GetAdminCustomerOrderTemplate");

adminCustomers.MapGet("/{customerId:guid}/frequent-products", async (
    Guid customerId,
    AdminCustomerCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetFrequentProductsAsync(customerId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("GetAdminCustomerFrequentProducts");

adminCustomers.MapPut("/{customerId:guid}/frequent-products", async (
    Guid customerId,
    UpdateCustomerFrequentProductsRequest request,
    ClaimsPrincipal user,
    AdminCustomerCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ReplaceFrequentProductsAsync(customerId, request, GetAdminActor(user), cancellationToken));
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
.WithName("UpdateAdminCustomerFrequentProducts");

adminCustomers.MapGet("/{customerId:guid}/machine-assignments", async (
    Guid customerId,
    AdminCustomerCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetMachineAssignmentsAsync(customerId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("GetAdminCustomerMachineAssignments");

adminCustomers.MapPut("/{customerId:guid}/machine-assignments", async (
    Guid customerId,
    UpdateCustomerMachineAssignmentsRequest request,
    ClaimsPrincipal user,
    AdminCustomerCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ReplaceMachineAssignmentsAsync(customerId, request, GetAdminActor(user), cancellationToken));
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
.WithName("UpdateAdminCustomerMachineAssignments");

adminCustomers.MapGet("/{customerId:guid}/access-tokens", async (
    Guid customerId,
    AdminCustomerAccessTokenService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetByCustomerAsync(customerId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("GetAdminCustomerAccessTokens");

adminCustomers.MapPost("/{customerId:guid}/access-tokens", async (
    Guid customerId,
    CreateCustomerAccessTokenRequest request,
    ClaimsPrincipal user,
    AdminCustomerAccessTokenService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await service.CreateAsync(customerId, request, GetAdminActor(user), cancellationToken);
        return Results.Created($"/api/admin/customers/{customerId}/access-tokens/{response.TokenId}", response);
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
.WithName("CreateAdminCustomerAccessToken");

adminCustomers.MapPatch("/{customerId:guid}/access-tokens/{tokenId:guid}/revoke", async (
    Guid customerId,
    Guid tokenId,
    ClaimsPrincipal user,
    AdminCustomerAccessTokenService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.RevokeAsync(customerId, tokenId, GetAdminActor(user), cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("RevokeAdminCustomerAccessToken");

adminCustomers.MapPost("/{customerId:guid}/orders/submit", async (
    Guid customerId,
    AdminSubmitCustomerOrderRequest request,
    ClaimsPrincipal user,
    AdminOrderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.SubmitCustomerOrderAsync(customerId, request, GetAdminActor(user), cancellationToken));
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
.WithName("SubmitAdminCustomerOrder");

adminCustomers.MapPost("/{customerId:guid}/orders/no-order", async (
    Guid customerId,
    AdminMarkNoOrderRequest request,
    ClaimsPrincipal user,
    AdminOrderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.MarkNoOrderAsync(customerId, request, GetAdminActor(user), cancellationToken));
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
.WithName("MarkAdminCustomerNoOrder");

var adminProducts = app.MapGroup("/api/admin/products")
    .WithTags("Admin products")
    .RequireAuthorization("AdminAccess");

adminProducts.MapGet("", async (
    AdminProductCatalogService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetAllAsync(cancellationToken));
})
.WithName("GetAdminProducts");

adminProducts.MapGet("/{productId:guid}", async (
    Guid productId,
    AdminProductCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetByIdAsync(productId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("GetAdminProduct");

adminProducts.MapPost("", async (
    UpsertAdminProductRequest request,
    ClaimsPrincipal user,
    AdminProductCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await service.CreateAsync(request, GetAdminActor(user), cancellationToken);
        return Results.Created($"/api/admin/products/{response.Id}", response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("CreateAdminProduct");

adminProducts.MapPut("/{productId:guid}", async (
    Guid productId,
    UpsertAdminProductRequest request,
    ClaimsPrincipal user,
    AdminProductCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.UpdateAsync(productId, request, GetAdminActor(user), cancellationToken));
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
.WithName("UpdateAdminProduct");

adminProducts.MapPatch("/{productId:guid}/activate", async (
    Guid productId,
    ClaimsPrincipal user,
    AdminProductCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ActivateAsync(productId, GetAdminActor(user), cancellationToken));
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
.WithName("ActivateAdminProduct");

adminProducts.MapPatch("/{productId:guid}/deactivate", async (
    Guid productId,
    ClaimsPrincipal user,
    AdminProductCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.DeactivateAsync(productId, GetAdminActor(user), cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("DeactivateAdminProduct");

var adminMachines = app.MapGroup("/api/admin/machines")
    .WithTags("Admin machines")
    .RequireAuthorization("AdminAccess");

adminMachines.MapGet("", async (
    AdminMachineCatalogService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetAllAsync(cancellationToken));
})
.WithName("GetAdminMachines");

adminMachines.MapGet("/{machineId:guid}", async (
    Guid machineId,
    AdminMachineCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.GetByIdAsync(machineId, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("GetAdminMachine");

adminMachines.MapPost("", async (
    UpsertAdminMachineRequest request,
    ClaimsPrincipal user,
    AdminMachineCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await service.CreateAsync(request, GetAdminActor(user), cancellationToken);
        return Results.Created($"/api/admin/machines/{response.Id}", response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("CreateAdminMachine");

adminMachines.MapPut("/{machineId:guid}", async (
    Guid machineId,
    UpsertAdminMachineRequest request,
    ClaimsPrincipal user,
    AdminMachineCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.UpdateAsync(machineId, request, GetAdminActor(user), cancellationToken));
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
.WithName("UpdateAdminMachine");

adminMachines.MapPatch("/{machineId:guid}/activate", async (
    Guid machineId,
    ClaimsPrincipal user,
    AdminMachineCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ActivateAsync(machineId, GetAdminActor(user), cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("ActivateAdminMachine");

adminMachines.MapPatch("/{machineId:guid}/deactivate", async (
    Guid machineId,
    ClaimsPrincipal user,
    AdminMachineCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.DeactivateAsync(machineId, GetAdminActor(user), cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("DeactivateAdminMachine");

var adminImports = app.MapGroup("/api/admin/import")
    .WithTags("Admin import")
    .RequireAuthorization("AdminAccess");

adminImports.MapGet("/templates", (AdminImportService service) =>
{
    return Results.Ok(service.GetTemplates());
})
.WithName("GetAdminImportTemplates");

adminImports.MapPost("/{importType}/validate", async (
    string importType,
    ImportCsvRequest request,
    AdminImportService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ValidateAsync(importType, request, cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("ValidateAdminImport");

adminImports.MapPost("/{importType}/apply", async (
    string importType,
    ImportCsvRequest request,
    ClaimsPrincipal user,
    AdminImportService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await service.ApplyAsync(importType, request, GetAdminActor(user), cancellationToken);
        return response.Errors.Count > 0
            ? Results.BadRequest(response)
            : Results.Ok(response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("ApplyAdminImport");

var adminUsers = app.MapGroup("/api/admin/users")
    .WithTags("Admin users")
    .RequireAuthorization("AdminAccess");

adminUsers.MapGet("", async (
    AdminUserCatalogService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetAllAsync(cancellationToken));
})
.WithName("GetAdminUsers");

adminUsers.MapPost("", async (
    CreateAdminUserRequest request,
    ClaimsPrincipal user,
    AdminUserCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await service.CreateAsync(request, GetAdminActor(user), cancellationToken);
        return Results.Created($"/api/admin/users/{response.Id}", response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("CreateAdminUser");

adminUsers.MapPatch("/{userId:guid}/activate", async (
    Guid userId,
    ClaimsPrincipal user,
    AdminUserCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ActivateAsync(userId, GetAdminActor(user), cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("ActivateAdminUser");

adminUsers.MapPatch("/{userId:guid}/deactivate", async (
    Guid userId,
    ClaimsPrincipal user,
    AdminUserCatalogService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.DeactivateAsync(userId, GetAdminActor(user), cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("DeactivateAdminUser");

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

static AdminActorContext GetAdminActor(ClaimsPrincipal user)
{
    return new AdminActorContext(
        user.FindFirstValue(ProdimtAuthClaims.UserId),
        user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name);
}

public partial class Program;
