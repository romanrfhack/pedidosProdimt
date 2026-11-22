using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class CollectionsInstallerSqlTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeInstallerQueriesVerifyRealUpgradeAndPreserveExistingMetadata(bool existingCounterRole)
    {
        var contract = await NativeContractAsync();
        var database = new SqlServerFixture { InitialMigration = "20261117145405_AddMachineWorkspace" };
        try
        {
            await database.InitializeAsync();
            var catalog = await database.CreateCatalogAsync();
            var existingRoleId = Guid.NewGuid();
            const string privateHash = "synthetic-private-hash-not-in-native-report";
            var user = AdminUser.Create("native-collections-" + Guid.NewGuid().ToString("N"),
                "Cuenta sintética privada", privateHash, SqlServerFixture.Now);
            await using (var db = database.CreateDbContext())
            {
                db.AdminUsers.Add(user);
                if (existingCounterRole)
                {
                    db.SecurityRoles.Add(new SecurityRole
                    {
                        Id = existingRoleId, Name = "Administración – Mostrador",
                        Description = "Configuración personalizada que debe conservarse", IsActive = true
                    });
                    db.SecurityUserRoles.Add(new SecurityUserRole { UserId = user.Id, RoleId = existingRoleId });
                }
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE USER [prodimt_pedidos_app] WITHOUT LOGIN;
                    GRANT SELECT ON dbo.SecurityRoles TO [prodimt_pedidos_app];
                    GRANT SELECT ON dbo.SecurityPermissions TO [prodimt_pedidos_app];
                    GRANT SELECT ON dbo.SecurityRolePermissions TO [prodimt_pedidos_app];
                    """);
            }

            var preservationQuery = contract.GetProperty("preservation").GetString()!;
            var preservation = await ReadJsonAsync(database, "SELECT (" + preservationQuery.TrimEnd().TrimEnd(';') + ") AS ProdimtInspectionJson;");
            AssertPreservationShape(preservation);
            await ValidateNativeMetadataAsync(preservation);
            Assert.DoesNotContain(privateHash, preservation.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(catalog.CustomerCode, preservation.GetRawText(), StringComparison.Ordinal);
            foreach (var section in new[] { "columns_metadata", "indexes_metadata", "sql_permissions", "unchanged_counts" })
                Assert.Equal(JsonValueKind.Array, preservation.GetProperty(section).ValueKind);

            var before = await ReadJsonAsync(database, contract.GetProperty("schema").GetString()!, asApplication: true);
            Assert.Equal(0, before.GetProperty("tables_count").GetInt32());
            Assert.Equal(0, before.GetProperty("checks_count").GetInt32());
            Assert.Equal(0, before.GetProperty("permissions_count").GetInt32());

            await using (var upgraded = database.CreateDbContext())
                await upgraded.Database.MigrateAsync();

            var schema = await ReadJsonAsync(database, contract.GetProperty("schema").GetString()!, asApplication: true);
            Assert.Equal(5, schema.GetProperty("tables_count").GetInt32());
            Assert.Equal(6, schema.GetProperty("checks_count").GetInt32());
            Assert.Equal(1, schema.GetProperty("receiver_present").GetInt32());
            Assert.Equal(3, schema.GetProperty("permissions_count").GetInt32());
            var permissions = await ReadJsonAsync(database, contract.GetProperty("permissions").GetString()!, asApplication: true);
            foreach (var expected in contract.GetProperty("expectedPermissions").EnumerateObject())
                Assert.Equal(expected.Value.GetInt32(), permissions.GetProperty(expected.Name).GetInt32());
            Assert.Equal(20, permissions.EnumerateObject().Count());

            var after = await ReadJsonAsync(database, contract.GetProperty("preservation").GetString()!);
            AssertPreservationShape(after);
            await ValidateNativeMetadataAsync(after);
            await VerifyPreservationAsync(preservation, after);
            Assert.DoesNotContain(privateHash, after.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(catalog.CustomerCode, after.GetRawText(), StringComparison.Ordinal);

            await using (var check = database.CreateDbContext())
            {
                Assert.Equal(privateHash, (await check.AdminUsers.SingleAsync(x => x.Id == user.Id)).PasswordHash);
                var cashierId = existingCounterRole ? existingRoleId : SecurityDefaults.CounterCashierRoleId;
                var grants = await (from grant in check.SecurityRolePermissions
                    join permission in check.SecurityPermissions on grant.PermissionId equals permission.Id
                    where grant.RoleId == cashierId select permission.Key).ToArrayAsync();
                Assert.Contains("operations.collections.receive", grants);
                Assert.Contains("operations.collections.read", grants);
                Assert.DoesNotContain("operations.collections.work", grants);
                Assert.Equal(existingCounterRole,
                    await check.SecurityUserRoles.AnyAsync(x => x.UserId == user.Id && x.RoleId == cashierId));
                await check.Database.MigrateAsync();
            }
            var replay = await ReadJsonAsync(database, contract.GetProperty("preservation").GetString()!);
            Assert.Equal(after.GetRawText(), replay.GetRawText());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePreservationGuardAcceptsEmptyUserSecurityTables(bool removeUsers)
    {
        var contract = await NativeContractAsync();
        var database = new SqlServerFixture { InitialMigration = "20261117145405_AddMachineWorkspace" };
        try
        {
            await database.InitializeAsync();
            await using (var db = database.CreateDbContext())
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.SecurityUserPermissions; DELETE FROM dbo.SecurityUserRoles;");
                if (removeUsers) await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.AdminUsers;");
            }
            var query = contract.GetProperty("preservation").GetString()!;
            var snapshot = await ReadJsonAsync(database, "SELECT (" + query.TrimEnd().TrimEnd(';') + ") AS ProdimtInspectionJson;");
            AssertPreservationShape(snapshot);
            await ValidateNativeMetadataAsync(snapshot);
            var emptyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.Unicode.GetBytes("[]")));
            Assert.Equal(emptyHash, snapshot.GetProperty("user_roles_hash").GetString());
            Assert.Equal(emptyHash, snapshot.GetProperty("user_permissions_hash").GetString());
            if (removeUsers) Assert.Equal(emptyHash, snapshot.GetProperty("users_hash").GetString());
            else Assert.NotEqual(emptyHash, snapshot.GetProperty("users_hash").GetString());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static void AssertPreservationShape(JsonElement snapshot)
    {
        var arrays = new[] { "columns_metadata", "indexes_metadata", "sql_permissions", "unchanged_counts",
            "security_roles", "security_permissions", "security_grants" };
        var hashes = new[] { "users_hash", "user_roles_hash", "user_permissions_hash" };
        var shape = string.Join(", ", snapshot.EnumerateObject().Select(x => x.Name + ":" + x.Value.ValueKind));
        Assert.True(snapshot.EnumerateObject().Count() == 10, "Native preservation metadata has unexpected fields. Shape only: " + shape);
        foreach (var name in arrays)
            Assert.True(snapshot.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array,
                "Native preservation array missing or invalid: " + name + ". Shape only: " + shape);
        foreach (var name in hashes)
        {
            Assert.True(snapshot.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String,
                "Native preservation hash missing or invalid: " + name + ". Shape only: " + shape);
            Assert.Matches("^[A-Fa-f0-9]{64}$", value.GetString()!);
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(SqlServerFixture database, string query, bool asApplication = false)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = asApplication
            ? "EXECUTE AS USER=N'prodimt_pedidos_app'; BEGIN TRY " + query + " REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH;"
            : query;
        await using var reader = await command.ExecuteReaderAsync();
        // SQL Server may split FOR JSON output across several rows.
        var json = new System.Text.StringBuilder();
        while (await reader.ReadAsync()) json.Append(reader.GetString(0));
        using var document = JsonDocument.Parse(json.ToString());
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> NativeContractAsync()
    {
        var start = NativePython();
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import json,runpy,sys;m=runpy.run_path(sys.argv[1]);print(json.dumps(dict(schema=m['schema_query'](),permissions=m['permission_query'](),preservation=m['preservation_query'](),expectedPermissions=m['expected_permissions']())))");
        start.ArgumentList.Add(NativeScriptPath());
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }

    private static async Task VerifyPreservationAsync(JsonElement before, JsonElement after)
    {
        var start = NativePython();
        start.RedirectStandardInput = true;
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import json,runpy,sys;m=runpy.run_path(sys.argv[1]);v=json.load(sys.stdin);m['verify_preservation'](v['before'],v['after']);print('preservation_verified')");
        start.ArgumentList.Add(NativeScriptPath());
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { before, after }));
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        Assert.Equal("preservation_verified", output.Trim());
    }

    private static async Task ValidateNativeMetadataAsync(JsonElement snapshot)
    {
        var start = NativePython();
        start.RedirectStandardInput = true;
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import json,runpy,sys;m=runpy.run_path(sys.argv[1]);m['validate_preservation_metadata'](json.load(sys.stdin));print('metadata_verified')");
        start.ArgumentList.Add(NativeScriptPath());
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(snapshot.GetRawText());
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        Assert.Equal("metadata_verified", output.Trim());
    }

    private static string NativeScriptPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        const string relative = "scripts/pilot/install-collections-update.py";
        while (root is not null && !File.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root!.FullName, relative);
    }

    private static ProcessStartInfo NativePython() => new("python3")
    {
        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
    };
}
