using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class NativeUpgradeSqlTests(SqlServerFixture fixture)
{
    private const string OldMigration = "20260528131416_AddCatalogExternalCodesAndImportSupport";
    private const string LatestMigration = "20261028202031_AddCustomerCaptureAliases";
    private const string ExistingHash = "synthetic-existing-password-hash-must-not-be-replaced";

    [Fact]
    public async Task FiveMigrationDatabase_BackupAndIdempotentUpgradePreserveDataAndRestrictedApplicationPermissions()
    {
        await using var database = new UpgradeDatabase(fixture.ConnectionString);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(OldMigration);
        Assert.Equal(5, (await db.Database.GetAppliedMigrationsAsync()).Count());

        var customerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        await using var admin = await database.OpenAsync();
        // Raw inserts intentionally use only columns that existed in the five-migration schema.
        await ExecuteAsync(admin, """
            INSERT INTO [Customers] ([Id],[Name],[PhoneNumber],[IsActive],[CreatedAt],[UpdatedAt],[ExternalCode])
            VALUES (@customer,N'Synthetic existing customer',N'5550000000',0,@now,@now,N'SYNTHETIC-OLD');
            INSERT INTO [AdminUsers] ([Id],[UserName],[PasswordHash],[DisplayName],[IsActive],[CreatedAt])
            VALUES (@admin,N'synthetic-existing-admin',@hash,N'Synthetic existing administrator',0,@now);
            CREATE USER [native_app] WITHOUT LOGIN;
            ALTER ROLE [db_datareader] ADD MEMBER [native_app];
            ALTER ROLE [db_datawriter] ADD MEMBER [native_app];
            """, new SqlParameter("@customer", customerId), new SqlParameter("@admin", adminId),
            new SqlParameter("@now", SqlServerFixture.Now), new SqlParameter("@hash", ExistingHash));
        var applicationSid = await ScalarAsync<byte[]>(admin,
            "SELECT [sid] FROM sys.database_principals WHERE [name] = N'native_app'");
        Assert.Equal(0, await ScalarAsync<int>(admin, "SELECT COUNT(*) FROM [Orders]"));
        Assert.False(await ScalarAsync<bool>(admin, """
            SELECT [is_unique] FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.Orders') AND [name] = N'IX_Orders_CustomerId_OrderDate_SequenceNumber'
            """));
        await VerifyApplicationPermissionsAsync(database, exerciseNewTables: false, customerId);

        // The path belongs to the disposable SQL container, not the .NET test host.
        // Its --rm cleanup removes this unique backup; no production path or xp_cmdshell is used.
        var backupPath = $"/var/opt/mssql/data/{database.Name}_{Guid.NewGuid():N}.bak";
        await ExecuteAsync(admin, $"BACKUP DATABASE [{database.Name}] TO DISK = @backup WITH COPY_ONLY, CHECKSUM",
            new SqlParameter("@backup", backupPath));
        await ExecuteAsync(admin, "RESTORE VERIFYONLY FROM DISK = @backup WITH CHECKSUM", new SqlParameter("@backup", backupPath));
        Assert.Equal(1, await ScalarAsync<int>(admin, """
            SELECT COUNT(*) FROM msdb.dbo.backupset
            WHERE [database_name] = @database AND [type] = N'D'
                AND [is_copy_only] = 1 AND [has_backup_checksums] = 1 AND [backup_size] > 0
            """, new SqlParameter("@database", database.Name)));

        var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains(LatestMigration, script);
        // Match the native deployment path: run the SQL script, then repeat that same script.
        // One open connection is required because transactions span GO-delimited batches.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            foreach (var batch in Regex.Split(script, @"^[ \t]*GO[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) await ExecuteAsync(admin, batch);
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Equal(11, applied.Length);
            Assert.Equal(LatestMigration, applied[^1]);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            await AssertPreservedDataAsync(db, customerId, adminId);
        }

        Assert.Equal(applicationSid, await ScalarAsync<byte[]>(admin,
            "SELECT [sid] FROM sys.database_principals WHERE [name] = N'native_app'"));
        Assert.True(await ScalarAsync<bool>(admin, """
            SELECT [is_unique] FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.Orders') AND [name] = N'IX_Orders_CustomerId_OrderDate_SequenceNumber'
            """));
        Assert.Equal(1, await ScalarAsync<int>(admin, """
            SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N'dbo.OrderChangeNotices')
            """));
        Assert.Equal(1, await ScalarAsync<int>(admin, """
            SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.OrderChangeNotices')
                AND [name] = N'IX_OrderChangeNotices_OrderId_OrderVersion_MachineId'
                AND [is_unique] = 1 AND [filter_definition] IS NULL
            """));
        await VerifyApplicationPermissionsAsync(database, exerciseNewTables: true, customerId);
        await AssertPreservedDataAsync(db, customerId, adminId);
    }

    private static async Task AssertPreservedDataAsync(PedidosDbContext db, Guid customerId, Guid adminId)
    {
        var customer = Assert.Single(await db.Customers.AsNoTracking().ToArrayAsync());
        Assert.Equal(customerId, customer.Id);
        Assert.Equal(0, customer.RestDaysMask);
        Assert.Equal("Synthetic existing customer", customer.Name);
        Assert.Equal("5550000000", customer.PhoneNumber);
        Assert.Equal("SYNTHETIC-OLD", customer.ExternalCode);
        Assert.False(customer.IsActive);
        Assert.Equal(SqlServerFixture.Now, customer.CreatedAt);
        Assert.Equal(SqlServerFixture.Now, customer.UpdatedAt);
        Assert.False(customer.IsOrderProfileApproved);
        Assert.Equal(0, customer.OrderingProfileVersion);
        var admin = Assert.Single(await db.AdminUsers.AsNoTracking().ToArrayAsync());
        Assert.Equal(adminId, admin.Id);
        Assert.Equal("synthetic-existing-admin", admin.UserName);
        Assert.Equal("Synthetic existing administrator", admin.DisplayName);
        Assert.Equal(ExistingHash, admin.PasswordHash);
        Assert.False(admin.IsActive);
        Assert.Equal(SqlServerFixture.Now, admin.CreatedAt);
        Assert.Equal(0, await db.Orders.CountAsync());
        Assert.Equal(0, await db.OrderOperations.CountAsync());
        Assert.Equal(0, await db.OrderChangeNotices.CountAsync());
        Assert.Equal(0, await db.SalesChannels.CountAsync());
    }

    private static async Task VerifyApplicationPermissionsAsync(UpgradeDatabase database, bool exerciseNewTables, Guid customerId)
    {
        // Database impersonation tests authorization, not SQL-login authentication. No server login is created.
        await using var application = await database.OpenAsync();
        await ExecuteAsync(application, "EXECUTE AS USER = N'native_app'");
        try
        {
            Assert.Equal("native_app", await ScalarAsync<string>(application, "SELECT USER_NAME()"));
            Assert.Equal(1, await ScalarAsync<int>(application, "SELECT IS_ROLEMEMBER(N'db_datareader')"));
            Assert.Equal(1, await ScalarAsync<int>(application, "SELECT IS_ROLEMEMBER(N'db_datawriter')"));
            Assert.Equal(0, await ScalarAsync<int>(application, "SELECT HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE TABLE')"));
            Assert.Equal(0, await ScalarAsync<int>(application, "SELECT HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'BACKUP DATABASE')"));
            Assert.Equal(1, await ScalarAsync<int>(application, "SELECT COUNT(*) FROM dbo.Customers"));
            var denied = await Assert.ThrowsAsync<SqlException>(() =>
                ExecuteAsync(application, "CREATE TABLE dbo.NativeUpgradeDdlMustBeDenied ([Id] int NOT NULL)"));
            Assert.Equal(262, denied.Number); // CREATE TABLE permission denied in this database.

            if (!exerciseNewTables) return;
            await ExecuteAsync(application, "BEGIN TRANSACTION");
            try
            {
                var orderId = Guid.NewGuid();
                var noticeId = Guid.NewGuid();
                await ExecuteAsync(application, """
                    DECLARE @channel uniqueidentifier = NEWID();
                    INSERT INTO dbo.SalesChannels ([Id],[Name],[Type],[IsInternal])
                    VALUES (@channel,N'Synthetic channel',N'AdminManualCapture',1);
                    INSERT INTO dbo.Orders ([Id],[CustomerId],[SalesChannelId],[OrderDate],[SubmittedAt],[Status],
                        [SequenceNumber],[IsLate],[RequiresAdminReview])
                    VALUES (@order,@customer,@channel,'2026-10-25',@now,N'Submitted',1,0,0);
                    INSERT INTO dbo.OrderOperations ([OperationKey],[PayloadHash],[ResultJson],[CreatedAt])
                    VALUES (N'native-upgrade-dml',N'synthetic-hash',N'{}',@now);
                    UPDATE dbo.OrderOperations SET [ResultJson] = N'{"verified":true}' WHERE [OperationKey] = N'native-upgrade-dml';
                    INSERT INTO dbo.OrderChangeNotices ([Id],[OrderId],[OrderVersion],[Version],[ServiceDate],[CustomerId],
                        [CustomerName],[Kind],[Reason],[OccurredAt],[LinesJson])
                    VALUES (@notice,@order,1,1,'2026-10-25',@customer,N'Synthetic existing customer',
                        N'correction',N'Synthetic permission check',@now,N'[]');
                    UPDATE dbo.OrderChangeNotices SET [Version] = 2, [AcknowledgedAt] = @now,
                        [AcknowledgedBy] = N'Synthetic operator' WHERE [Id] = @notice;
                    """, new SqlParameter("@order", orderId), new SqlParameter("@customer", customerId),
                    new SqlParameter("@notice", noticeId), new SqlParameter("@now", SqlServerFixture.Now));
                Assert.Equal("{\"verified\":true}", await ScalarAsync<string>(application,
                    "SELECT [ResultJson] FROM dbo.OrderOperations WHERE [OperationKey] = N'native-upgrade-dml'"));
                Assert.Equal(2, await ScalarAsync<int>(application,
                    "SELECT [Version] FROM dbo.OrderChangeNotices WHERE [Id] = @notice", new SqlParameter("@notice", noticeId)));
                Assert.Equal(1, await ExecuteAsync(application,
                    "DELETE FROM dbo.OrderChangeNotices WHERE [Id] = @notice", new SqlParameter("@notice", noticeId)));
                Assert.Equal(1, await ExecuteAsync(application,
                    "DELETE FROM dbo.OrderOperations WHERE [OperationKey] = N'native-upgrade-dml'"));
            }
            finally { await ExecuteAsync(application, "IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION"); }
        }
        finally { await ExecuteAsync(application, "REVERT"); }
    }

    private static async Task<int> ExecuteAsync(SqlConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed class UpgradeDatabase : IAsyncDisposable
    {
        private readonly string connectionString;
        public string Name { get; } = $"ProdimtNativeUpgradeTest_{Guid.NewGuid():N}";

        public UpgradeDatabase(string fixtureConnectionString)
        {
            // Ignore the configured/fixture catalog; all writes target this independently generated database.
            connectionString = new SqlConnectionStringBuilder(fixtureConnectionString)
            {
                InitialCatalog = Name,
                Pooling = false
            }.ConnectionString;
        }

        public PedidosDbContext CreateDbContext() => new(new DbContextOptionsBuilder<PedidosDbContext>()
            .UseSqlServer(connectionString).Options);

        public async Task<SqlConnection> OpenAsync()
        {
            var connection = new SqlConnection(connectionString);
            try { await connection.OpenAsync(); return connection; }
            catch { await connection.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
