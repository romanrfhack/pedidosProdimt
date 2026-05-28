using System.IdentityModel.Tokens.Jwt;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class PilotAuthenticationServiceTests
{
    [Fact]
    public async Task LoginAdminAsync_WithValidCredentials_ReturnsJwt()
    {
        await using var fixture = await AuthFixture.CreateAsync();

        var response = await fixture.Service.LoginAdminAsync(
            new AdminLoginRequest(DevelopmentAuthSeedValues.DefaultAdminUserName, DevelopmentAuthSeedValues.DefaultAdminPassword),
            CancellationToken.None);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);

        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal("Administrador Demo", response.DisplayName);
        Assert.Contains(token.Claims, x => x.Type == ProdimtAuthClaims.ActorType && x.Value == ProdimtActorTypes.Admin);
        Assert.Contains(token.Claims, x => x.Type == ProdimtAuthClaims.UserName && x.Value == "admin");
    }

    [Fact]
    public async Task LoginAdminAsync_WithInvalidCredentials_ThrowsAuthenticationFailed()
    {
        await using var fixture = await AuthFixture.CreateAsync();

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => fixture.Service.LoginAdminAsync(
            new AdminLoginRequest("admin", "wrong-password"),
            CancellationToken.None));
    }

    [Fact]
    public async Task LoginCustomerWithTokenAsync_WithValidToken_ReturnsJwtWithCustomerId()
    {
        await using var fixture = await AuthFixture.CreateAsync();

        var response = await fixture.Service.LoginCustomerWithTokenAsync(
            new CustomerTokenLoginRequest(DevelopmentAuthSeedValues.DefaultCustomerToken),
            CancellationToken.None);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);

        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal(DevelopmentSeedIds.GranTakitoCustomerId, response.CustomerId);
        Assert.Equal("Gran Takito", response.CustomerName);
        Assert.Contains(token.Claims, x => x.Type == ProdimtAuthClaims.ActorType && x.Value == ProdimtActorTypes.Customer);
        Assert.Contains(token.Claims, x => x.Type == ProdimtAuthClaims.CustomerId && x.Value == DevelopmentSeedIds.GranTakitoCustomerId.ToString());
    }

    [Fact]
    public async Task LoginCustomerWithTokenAsync_WithInvalidToken_ThrowsAuthenticationFailed()
    {
        await using var fixture = await AuthFixture.CreateAsync();

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => fixture.Service.LoginCustomerWithTokenAsync(
            new CustomerTokenLoginRequest("invalid-token"),
            CancellationToken.None));
    }

    private sealed class AuthFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private AuthFixture(SqliteConnection connection, PedidosDbContext dbContext, PilotAuthenticationService service)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = service;
        }

        public PedidosDbContext DbContext { get; }

        public PilotAuthenticationService Service { get; }

        public static async Task<AuthFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<PedidosDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new PedidosDbContext(options);
            var configuration = CreateConfiguration();
            var passwordHashService = new PasswordHashService();
            var customerAccessTokenHasher = new CustomerAccessTokenHasher();
            var dateTimeProvider = new FixedDateTimeProvider(new DateTimeOffset(2026, 5, 28, 9, 0, 0, TimeSpan.Zero));

            await dbContext.Database.EnsureCreatedAsync();
            await PedidosDevelopmentSeeder.SeedAsync(
                dbContext,
                configuration,
                passwordHashService,
                customerAccessTokenHasher);

            var service = new PilotAuthenticationService(
                new EfAdminUserRepository(dbContext),
                new EfCustomerAccessTokenRepository(dbContext),
                customerAccessTokenHasher,
                new EfCustomerRepository(dbContext),
                passwordHashService,
                new JwtTokenService(configuration, dateTimeProvider),
                dateTimeProvider);

            return new AuthFixture(connection, dbContext, service);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static IConfiguration CreateConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Jwt:SigningKey"] = JwtSettings.DevelopmentSigningKey,
                    ["Authentication:Jwt:Issuer"] = "Prodimt.Pedidos.Tests",
                    ["Authentication:Jwt:Audience"] = "Prodimt.Pedidos.Tests",
                    ["Authentication:Jwt:AccessTokenMinutes"] = "60"
                })
                .Build();
        }
    }

    private sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset Now { get; } = now;

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
    }
}
