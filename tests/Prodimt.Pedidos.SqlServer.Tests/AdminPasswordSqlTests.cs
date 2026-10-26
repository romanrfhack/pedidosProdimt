using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class AdminPasswordSqlTests(SqlServerFixture fixture)
{
    private const string Current = "Fictional-current-password-2026!";
    private const string Next = "Fictional-replacement-password-2026!";
    private static AuditLog Audit(Guid id) => AuditLog.Create("AdminUser", id.ToString(), "AdminUserPasswordChanged",
        DateTimeOffset.UtcNow, AuditActorType.Admin, "Contraseña administrativa actualizada.", id.ToString(), "Fixture");

    private async Task<AdminUser> CreateAsync()
    {
        var user = AdminUser.Create("pw-fixture-" + Guid.NewGuid().ToString("N"), "Password fixture",
            new PasswordHashService().HashPassword(Current), DateTimeOffset.UtcNow);
        await using var db = fixture.CreateDbContext();
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task HttpChange_PersistsAcrossHostsAndRejectsOldPasswordAndToken()
    {
        var user = await CreateAsync();
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.PostAsJsonAsync("/api/auth/admin/login", new { userName = user.UserName, password = Current });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<AdminLoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/admin/account/change-password",
            new ChangeAdminPasswordRequest(Current, Next, Next))).StatusCode);
        await using var second = fixture.CreateFactory();
        using var fresh = second.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        fresh.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.GetAsync("/api/admin/users")).StatusCode);
        fresh.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostAsJsonAsync("/api/auth/admin/login", new { userName = user.UserName, password = Current })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsJsonAsync("/api/auth/admin/login", new { userName = user.UserName, password = Next })).StatusCode);
        await using var db = fixture.CreateDbContext();
        var stored = await db.AdminUsers.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        Assert.Equal(user.DisplayName, stored.DisplayName);
        Assert.Equal(user.CreatedAt, stored.CreatedAt);
        var audit = Assert.Single(await db.AuditLogs.Where(x => x.EntityId == user.Id.ToString()).ToArrayAsync());
        Assert.Equal("AdminUserPasswordChanged", audit.EventType);
        Assert.Null(audit.MetadataJson);
    }

    [Fact]
    public async Task ConcurrentPasswordChanges_OneWinsAndOneAuditIsRecorded()
    {
        var user = await CreateAsync();
        async Task<bool> Attempt(string next)
        {
            await using var db = fixture.CreateDbContext();
            return await new EfAdminUserRepository(db).TryChangePasswordAsync(user.Id, user.PasswordHash,
                new PasswordHashService().HashPassword(next), Audit(user.Id), default);
        }
        var results = await Task.WhenAll(Attempt(Next), Attempt(Next + "other"));
        Assert.Equal(1, results.Count(x => x));
        await using var verify = fixture.CreateDbContext();
        Assert.Single(await verify.AuditLogs.Where(x => x.EntityId == user.Id.ToString()).ToArrayAsync());
    }

    [Fact]
    public async Task AuditFailure_RollsBackHashAndAllowsOldPassword()
    {
        var user = await CreateAsync();
        await using var db = fixture.CreateDbContext();
        var trigger = "PasswordAudit_" + Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER [{trigger}] ON [AuditLogs] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE EntityId='{user.Id}') THROW 51007,N'Fictional audit failure',1; END;");
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => new EfAdminUserRepository(db).TryChangePasswordAsync(
                user.Id, user.PasswordHash, new PasswordHashService().HashPassword(Next), Audit(user.Id), default));
        }
        finally { await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER [{trigger}];"); }
        await using var verify = fixture.CreateDbContext();
        var stored = await verify.AdminUsers.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        Assert.Equal(user.PasswordHash, stored.PasswordHash);
        Assert.True(new PasswordHashService().VerifyPassword(stored.PasswordHash, Current));
        Assert.Empty(await verify.AuditLogs.Where(x => x.EntityId == user.Id.ToString()).ToArrayAsync());
    }
}
