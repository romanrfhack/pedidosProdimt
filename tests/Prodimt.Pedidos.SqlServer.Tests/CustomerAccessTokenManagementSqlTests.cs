using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence.Migrations;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class CustomerAccessTokenManagementSqlTests : IAsyncLifetime
{
    private const string I11 = "20261101154618_AddSharedProductsAndCustomerPrices";
    private const string I12 = "20261101190000_AddRecoverableCustomerAccessTokens";
    private const string RevocationEvent = "CustomerAccessTokenRevokedForRecoveryUpgrade";
    private const string TestKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private readonly SqlServerFixture database = new();
    private static readonly CancellationToken Ct = CancellationToken.None;

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task MigrationRevokesEveryPriorEnabledAccessOnce_AndCatchupPreservesProtectedAccesses()
    {
        var (catalog, legacy) = await PrepareLegacyI11();
        var historicalAuditId = Guid.NewGuid();
        await using (var before = database.CreateDbContext())
        {
            before.AuditLogs.Add(new AuditLog { Id = historicalAuditId, EntityType = "SyntheticHistory",
                EntityId = catalog.CustomerId.ToString(), EventType = "SyntheticBeforeI12",
                OccurredAt = SqlServerFixture.Now, ActorType = AuditActorType.System, Summary = "Historical audit retained." });
            await before.SaveChangesAsync();
            await before.GetService<IMigrator>().MigrateAsync(I12);
        }
        await using (var upgraded = database.CreateDbContext())
        {
            Assert.Equal(13, (await upgraded.Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(I12, (await upgraded.Database.GetAppliedMigrationsAsync()).Last());
            var tokens = await upgraded.CustomerAccessTokens.AsNoTracking().ToDictionaryAsync(x => x.Id);
            Assert.Equal(legacy.Count, tokens.Count);
            foreach (var original in legacy)
            {
                var token = tokens[original.Id];
                Assert.False(token.IsActive); // Includes enabled-but-expired and no-expiration accesses.
                Assert.Equal(original.IsActive ? 2 : 1, token.MetadataVersion);
                Assert.Null(token.ProtectedToken);
                Assert.False(token.IsReminderLink);
                Assert.Equal(original.TokenHash, token.TokenHash);
                Assert.Equal(original.CustomerId, token.CustomerId);
                Assert.Equal(original.DisplayName, token.DisplayName);
                Assert.Equal(original.CreatedAt, token.CreatedAt);
                Assert.Equal(original.ExpiresAt, token.ExpiresAt);
                Assert.Equal(original.LastUsedAt, token.LastUsedAt);
            }
            var audits = await upgraded.AuditLogs.Where(x => x.EventType == RevocationEvent).ToArrayAsync();
            Assert.Equal(legacy.Count(x => x.IsActive), audits.Length);
            Assert.All(audits, x => { Assert.Equal(AuditActorType.System, x.ActorType); Assert.Equal(TimeSpan.Zero, x.OccurredAt.Offset); });
            Assert.Equal(legacy.Where(x => x.IsActive).Select(x => x.Id).Order(),
                audits.Select(x => Guid.Parse(x.EntityId)).Order());
            Assert.True(await upgraded.AuditLogs.AnyAsync(x => x.Id == historicalAuditId));
            Assert.Single(await upgraded.Customers.ToArrayAsync());
            Assert.Equal(2, await upgraded.Products.CountAsync());
            Assert.Single(await upgraded.AdminUsers.Select(x => x.Id).ToArrayAsync());
            Assert.Empty(await upgraded.Orders.ToArrayAsync());
            Assert.Empty(await upgraded.OrderOperations.ToArrayAsync());
        }

        var plain = "synthetic-protected-access-after-i12";
        var current = CustomerAccessToken.Create(catalog.CustomerId, new CustomerAccessTokenHasher().HashToken(plain),
            "New protected access", DateTimeOffset.UtcNow.AddDays(1), SqlServerFixture.Now, true);
        current.ProtectedToken = Protector().Protect(current.CustomerId, current.Id, plain);
        await using (var later = database.CreateDbContext())
        {
            later.CustomerAccessTokens.Add(current);
            await later.SaveChangesAsync();
            await later.GetService<IMigrator>().MigrateAsync(I12); // Migration history prevents a second global revocation.
        }
        var rollbackLegacy = CustomerAccessToken.Create(catalog.CustomerId, "synthetic-rollback-legacy-hash",
            "Legacy application retry", null, SqlServerFixture.Now);
        await InsertLegacy(rollbackLegacy); // Simulate old binary omitting the three new columns after app rollback.
        await Execute("SET XACT_ABORT ON; BEGIN TRANSACTION;\n" + AddRecoverableCustomerAccessTokens.RevokeLegacySql + "\nCOMMIT TRANSACTION;");
        await Execute("SET XACT_ABORT ON; BEGIN TRANSACTION;\n" + AddRecoverableCustomerAccessTokens.RevokeLegacySql + "\nCOMMIT TRANSACTION;");
        await using var final = database.CreateDbContext();
        var recovered = await final.CustomerAccessTokens.AsNoTracking().SingleAsync(x => x.Id == current.Id);
        Assert.True(recovered.IsActive);
        Assert.True(recovered.IsReminderLink);
        Assert.Equal(1, recovered.MetadataVersion);
        Assert.Equal(current.ProtectedToken, recovered.ProtectedToken);
        Assert.Equal(plain, Protector().Unprotect(recovered.CustomerId, recovered.Id, recovered.ProtectedToken!));
        Assert.False((await final.CustomerAccessTokens.SingleAsync(x => x.Id == rollbackLegacy.Id)).IsActive);
        Assert.Equal(legacy.Count(x => x.IsActive) + 1, await final.AuditLogs.CountAsync(x => x.EventType == RevocationEvent));
    }

    [Fact]
    public async Task MigrationAuditFailure_RollsBackColumnsRevocationAndHistory_ThenRetrySucceeds()
    {
        var (_, legacy) = await PrepareLegacyI11();
        await Execute("""
            CREATE TRIGGER reject_i12_revocation_audit ON dbo.AuditLogs AFTER INSERT AS
            BEGIN
             IF EXISTS (SELECT 1 FROM inserted WHERE EventType=N'CustomerAccessTokenRevokedForRecoveryUpgrade')
              THROW 51032,'synthetic_i12_audit_failure',1;
            END;
            """);
        try
        {
            await using var attempt = database.CreateDbContext();
            await Assert.ThrowsAsync<SqlException>(() => attempt.GetService<IMigrator>().MigrateAsync(I12));
            Assert.Equal(12, (await attempt.Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(1, await Scalar<int>("SELECT CASE WHEN COL_LENGTH(N'dbo.CustomerAccessTokens',N'ProtectedToken') IS NULL THEN 1 ELSE 0 END"));
            Assert.Equal(legacy.Count(x => x.IsActive), await Scalar<int>("SELECT COUNT(*) FROM dbo.CustomerAccessTokens WHERE IsActive=1"));
            Assert.Equal(0, await Scalar<int>("SELECT COUNT(*) FROM dbo.AuditLogs WHERE EventType=N'CustomerAccessTokenRevokedForRecoveryUpgrade'"));
        }
        finally { await Execute("DROP TRIGGER dbo.reject_i12_revocation_audit;"); }
        await using var retry = database.CreateDbContext();
        await retry.GetService<IMigrator>().MigrateAsync(I12);
        Assert.Equal(13, (await retry.Database.GetAppliedMigrationsAsync()).Count());
        Assert.False(await retry.CustomerAccessTokens.AnyAsync(x => x.IsActive));
        Assert.Equal(legacy.Count(x => x.IsActive), await retry.AuditLogs.CountAsync(x => x.EventType == RevocationEvent));
    }

    [Fact]
    public async Task NativeSchemaAndLegacyCatchup_UseExactInstallerSqlAndPreserveProtectedAccesses()
    {
        // The historical installer deliberately accepts exactly thirteen migrations.
        await using (var pinned = database.CreateDbContext()) await pinned.GetService<IMigrator>().MigrateAsync(I12);
        var contract = await NativeContract();
        var expected = JsonSerializer.Deserialize<Dictionary<string, int>>(contract.GetProperty("expectedSchema"))!;
        var observed = JsonSerializer.Deserialize<Dictionary<string, int>>(await Scalar<string>(contract.GetProperty("schema").GetString()!))!;
        Assert.Equal(expected.OrderBy(x => x.Key), observed.OrderBy(x => x.Key));
        var catalog = await database.CreateCatalogAsync();
        var legacy = CustomerAccessToken.Create(catalog.CustomerId, "synthetic-native-catchup-hash", "Legacy retry", null, SqlServerFixture.Now);
        await InsertLegacy(legacy);
        var current = CustomerAccessToken.Create(catalog.CustomerId, "synthetic-current-protected-hash", "Protected access", null, SqlServerFixture.Now);
        current.ProtectedToken = Protector().Protect(current.CustomerId, current.Id, "synthetic-current-protected-secret");
        await using (var setup = database.CreateDbContext()) { setup.Add(current); await setup.SaveChangesAsync(); }
        var cleanup = contract.GetProperty("cleanup").GetString()!;
        const string guard = "DB_NAME()<>N'ProdimtPedidos'";
        Assert.Contains(guard, cleanup);
        var target = new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog;
        Assert.StartsWith("ProdimtI02Test_", target);
        cleanup = cleanup.Replace(guard, $"DB_NAME()<>N'{target}'", StringComparison.Ordinal);
        await Execute("""
            CREATE TRIGGER reject_i12_native_cleanup ON dbo.AuditLogs AFTER INSERT AS
            BEGIN
             IF EXISTS (SELECT 1 FROM inserted WHERE EventType=N'CustomerAccessTokenRevokedForRecoveryUpgrade')
              THROW 51033,'synthetic_i12_native_cleanup_audit_failure',1;
            END;
            """);
        try
        {
            await Assert.ThrowsAsync<SqlException>(() => Scalar<string>(cleanup));
            await using var unchanged = database.CreateDbContext();
            Assert.True((await unchanged.CustomerAccessTokens.SingleAsync(x => x.Id == legacy.Id)).IsActive);
            Assert.True((await unchanged.CustomerAccessTokens.SingleAsync(x => x.Id == current.Id)).IsActive);
            Assert.False(await unchanged.AuditLogs.AnyAsync(x => x.EventType == RevocationEvent));
        }
        finally { await Execute("DROP TRIGGER dbo.reject_i12_native_cleanup;"); }
        using (var first = JsonDocument.Parse(await Scalar<string>(cleanup)))
        {
            Assert.Equal(1, first.RootElement.GetProperty("revoked").GetInt32());
            Assert.Equal(1, first.RootElement.GetProperty("transaction_committed").GetInt32());
        }
        using (var repeated = JsonDocument.Parse(await Scalar<string>(cleanup)))
            Assert.Equal(0, repeated.RootElement.GetProperty("revoked").GetInt32());
        await using var verified = database.CreateDbContext();
        var old = await verified.CustomerAccessTokens.SingleAsync(x => x.Id == legacy.Id);
        Assert.False(old.IsActive); Assert.Equal(2, old.MetadataVersion);
        var retained = await verified.CustomerAccessTokens.SingleAsync(x => x.Id == current.Id);
        Assert.True(retained.IsActive); Assert.Equal(1, retained.MetadataVersion);
        Assert.Equal(current.ProtectedToken, retained.ProtectedToken);
        var audit = Assert.Single(await verified.AuditLogs.Where(x => x.EventType == RevocationEvent).ToArrayAsync());
        Assert.Equal(AuditActorType.System, audit.ActorType);
        Assert.Equal(legacy.Id, Guid.Parse(audit.EntityId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataConcurrency_StaleEditOrRevocationRollsBackItsAudit(bool firstRevokes)
    {
        var catalog = await database.CreateCatalogAsync();
        var token = CustomerAccessToken.Create(catalog.CustomerId, "synthetic-concurrency-hash", "Original", null, SqlServerFixture.Now);
        token.ProtectedToken = Protector().Protect(token.CustomerId, token.Id, "synthetic-concurrency-secret");
        await using (var seed = database.CreateDbContext()) { seed.Add(token); await seed.SaveChangesAsync(); }
        await using var first = database.CreateDbContext();
        await using var stale = database.CreateDbContext();
        var firstRepo = new EfCustomerAccessTokenRepository(first);
        var staleRepo = new EfCustomerAccessTokenRepository(stale);
        var firstToken = (await firstRepo.GetByIdAsync(token.Id, Ct))!;
        var staleToken = (await staleRepo.GetByIdAsync(token.Id, Ct))!;
        async Task<int> Mutate(EfCustomerAccessTokenRepository repo, Prodimt.Pedidos.Infrastructure.Persistence.PedidosDbContext db,
            CustomerAccessToken target, bool revoke, string auditEvent)
            => await repo.ExecuteMutationAsync(async ct =>
            {
                if (revoke) target.Revoke();
                else { target.DisplayName = "Updated"; target.MetadataVersion++; }
                db.AuditLogs.Add(AuditLog.Create("CustomerAccessToken", target.Id.ToString(), auditEvent,
                    SqlServerFixture.Now, AuditActorType.Admin, "Synthetic metadata mutation."));
                await repo.SaveChangesAsync(ct);
                return 1;
            }, Ct);
        await Mutate(firstRepo, first, firstToken, firstRevokes, "SyntheticWinningMutation");
        await Assert.ThrowsAsync<CatalogConflictException>(() => Mutate(staleRepo, stale, staleToken, !firstRevokes, "SyntheticStaleMutation"));
        await using var verify = database.CreateDbContext();
        var actual = await verify.CustomerAccessTokens.AsNoTracking().SingleAsync(x => x.Id == token.Id);
        Assert.Equal(!firstRevokes, actual.IsActive);
        Assert.Equal(2, actual.MetadataVersion);
        Assert.Equal(firstRevokes ? "Original" : "Updated", actual.DisplayName);
        Assert.Equal(token.TokenHash, actual.TokenHash);
        Assert.Equal(token.ProtectedToken, actual.ProtectedToken);
        Assert.Equal(1, await verify.AuditLogs.CountAsync(x => x.EventType == "SyntheticWinningMutation"));
        Assert.False(await verify.AuditLogs.AnyAsync(x => x.EventType == "SyntheticStaleMutation"));
    }

    [Fact]
    public async Task ProtectedAccess_ReopensAcrossHosts_AndMetadataEditPreservesOriginalSecret()
    {
        var catalog = await database.CreateCatalogAsync();
        var url = $"/api/admin/customers/{catalog.CustomerId}/access-tokens";
        CreatedCustomerAccessTokenResponse created;
        await using (var firstHost = database.CreateFactory())
        using (var admin = await SqlServerFixture.CreateAdminClientAsync(firstHost))
        {
            using var response = await admin.PostAsJsonAsync(url, new CreateCustomerAccessTokenRequest("Synthetic reusable access", DateTimeOffset.UtcNow.AddHours(2)));
            response.EnsureSuccessStatusCode();
            created = (await response.Content.ReadFromJsonAsync<CreatedCustomerAccessTokenResponse>())!;
            var listing = await admin.GetStringAsync(url + "?activeOnly=true");
            Assert.DoesNotContain(created.PlainToken, listing);
            Assert.DoesNotContain("protectedToken", listing, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tokenHash", listing, StringComparison.OrdinalIgnoreCase);
        }
        string originalCiphertext;
        string originalHash;
        await using (var stored = database.CreateDbContext())
        {
            var row = await stored.CustomerAccessTokens.SingleAsync(x => x.Id == created.TokenId);
            originalCiphertext = row.ProtectedToken!; originalHash = row.TokenHash;
            Assert.NotEqual(created.PlainToken, originalCiphertext);
        }
        await using var secondHost = database.CreateFactory();
        using var secondAdmin = await SqlServerFixture.CreateAdminClientAsync(secondHost);
        using var reveal = await secondAdmin.PostAsJsonAsync(url + $"/{created.TokenId}/reveal", new { });
        reveal.EnsureSuccessStatusCode();
        Assert.Contains("no-store", reveal.Headers.CacheControl!.ToString());
        var recovered = (await reveal.Content.ReadFromJsonAsync<CreatedCustomerAccessTokenResponse>())!;
        Assert.Equal(created.PlainToken, recovered.PlainToken);
        var expiry = DateTimeOffset.UtcNow.AddHours(3);
        using var update = await secondAdmin.PatchAsJsonAsync(url + $"/{created.TokenId}", new UpdateCustomerAccessTokenRequest(created.Version, "Updated reusable access", expiry));
        update.EnsureSuccessStatusCode();
        using var stale = await secondAdmin.PatchAsJsonAsync(url + $"/{created.TokenId}", new UpdateCustomerAccessTokenRequest(created.Version, "Stale edit", expiry));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var again = await secondAdmin.PostAsJsonAsync(url + $"/{created.TokenId}/reveal", new { });
        again.EnsureSuccessStatusCode();
        Assert.Equal(created.PlainToken, (await again.Content.ReadFromJsonAsync<CreatedCustomerAccessTokenResponse>())!.PlainToken);
        await using var final = database.CreateDbContext();
        var persisted = await final.CustomerAccessTokens.AsNoTracking().SingleAsync(x => x.Id == created.TokenId);
        Assert.Equal(originalHash, persisted.TokenHash);
        Assert.Equal(originalCiphertext, persisted.ProtectedToken);
        Assert.Equal(2, persisted.MetadataVersion);
        Assert.Equal(expiry, persisted.ExpiresAt);
        var auditJson = JsonSerializer.Serialize(await final.AuditLogs.ToArrayAsync());
        Assert.DoesNotContain(created.PlainToken, auditJson);
        Assert.DoesNotContain(originalCiphertext, auditJson);
        Assert.DoesNotContain(originalHash, auditJson);
    }

    private async Task<(SqlServerFixture.TestCatalog Catalog, List<CustomerAccessToken> Legacy)> PrepareLegacyI11()
    {
        await using (var db = database.CreateDbContext()) await db.GetService<IMigrator>().MigrateAsync(I11);
        var catalog = await database.CreateCatalogAsync();
        var legacy = Enumerable.Range(0, 4).Select(index => CustomerAccessToken.Create(catalog.CustomerId,
            "synthetic-legacy-hash-" + index, "Synthetic legacy " + index,
            index == 0 ? null : SqlServerFixture.Now.AddDays(index == 1 ? -1 : 1), SqlServerFixture.Now.AddDays(-3))).ToList();
        legacy[^1].IsActive = false;
        legacy[0].LastUsedAt = SqlServerFixture.Now.AddHours(-1);
        foreach (var token in legacy) await InsertLegacy(token);
        return (catalog, legacy);
    }

    private async Task InsertLegacy(CustomerAccessToken token)
    {
        await using var db = database.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.CustomerAccessTokens(Id,CustomerId,TokenHash,DisplayName,IsActive,ExpiresAt,CreatedAt,LastUsedAt)
            VALUES({token.Id},{token.CustomerId},{token.TokenHash},{token.DisplayName},{token.IsActive},
              {token.ExpiresAt},{token.CreatedAt},{token.LastUsedAt});
            """);
    }

    private async Task Execute(string sql)
    {
        // Execute the reviewed SQL verbatim, including JSON metadata braces.
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> Scalar<T>(string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static AesGcmCustomerAccessTokenProtector Protector() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [AesGcmCustomerAccessTokenProtector.ConfigurationKey] = TestKey }).Build());

    private static async Task<JsonElement> NativeContract()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        const string relative = "scripts/pilot/install-access-token-management-update.py";
        while (root is not null && !File.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import json,runpy,sys;m=runpy.run_path(sys.argv[1]);print(json.dumps(dict(schema=m['TOKEN_SCHEMA'],expectedSchema=m['EXPECTED_TOKEN_SCHEMA'],cleanup=m['CLEANUP_LEGACY_SQL'])))");
        start.ArgumentList.Add(Path.Combine(root!.FullName, relative));
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        using var json = JsonDocument.Parse(output);
        return json.RootElement.Clone();
    }
}
