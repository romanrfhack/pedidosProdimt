using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Prodimt.Pedidos.SqlServer.Tests;

namespace Prodimt.Pedidos.DeploymentValidation;

/// <summary>
/// Runs the existing SQL/HTTP cases without a test SDK on the destination host.
/// The only writable SQL database has a name generated inside SqlServerFixture.
/// stdout is a fixed, secret-free protocol consumed by the native installer.
/// </summary>
internal static partial class ValidationRunner
{
    private const string ConnectionVariable = "PRODIMT_SQL_TEST_CONNECTION";
    private const string Protocol = "prodimt.delivery.validation.v1";
    private const string ConsolidationProtocol = "prodimt.consolidation.c1.validation.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--probe-c2"]) return await C2Validation.Probe();
        if (args is ["--list-c2"])
        {
            Console.WriteLine(JsonSerializer.Serialize(Prodimt.Pedidos.C2.Tests.C2Scenarios.Names));
            return 0;
        }
        if (args is ["--validate-c2" or "--serve-c2", var configuration])
            return await C2Validation.Run(args[0], configuration);

        var output = Console.Out;
        var error = Console.Error;
        var passed = new List<string>();
        var generatedDatabases = new HashSet<string>(StringComparer.Ordinal);
        var consolidation = args is ["--validate-c1"];
        var cleanupVerified = false;
        var expected = args is ["--self-test"] ? 1 : consolidation ? 7 : 3;
        string? issue = null;
        string? normalizedConnection = null;
        SqlServerFixture? fixture = null;
        var previousConnection = Environment.GetEnvironmentVariable(ConnectionVariable);
        var phase = "portable_files_invalid";

        // Existing API and EF diagnostics may contain connection context.
        // Only the fixed protocol below is permitted out of this child process.
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        void TrackDatabase(string name)
        {
            if (!GeneratedDatabase().IsMatch(name) || !generatedDatabases.Add(name))
                throw new ValidationFailure("isolated_database_name_invalid");
            output.WriteLine("DATABASE " + name);
            output.Flush();
        }
        SqlServerFixture.DisposableDatabaseCreating += TrackDatabase;
        try
        {
            if (args is not (["--validate"] or ["--validate-c1"] or ["--self-test"]))
                throw new ValidationFailure("invalid_arguments");

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            RequirePortableFiles();
            if (args is ["--self-test"])
            {
                phase = "portable_host_failed";
                await VerifyPortableHostAsync();
                passed.Add("portable_host_without_sql_or_sdk");
                cleanupVerified = true;
            }
            else
            {
                normalizedConnection = NormalizeConnection(previousConnection);
                Environment.SetEnvironmentVariable(ConnectionVariable, normalizedConnection);
                fixture = new SqlServerFixture();
                // This generated, non-business identifier lets the supervising
                // installer clean up even if it must terminate a timed-out child.
                phase = "initialize_isolated_database_failed";
                await fixture.InitializeAsync();
                var cases = new DeliveryManagementSqlTests(fixture);

                phase = "concurrent_reservations_failed";
                await cases.IndependentHostsCannotReserveTheSameRemainingDemandTwice();
                passed.Add("concurrent_reservations");

                phase = "own_scope_durable_replay_failed";
                await cases.DriverAuthorizationAndDurableDeliveryReplaySurviveDifferentHosts();
                passed.Add("own_scope_durable_replay");

                phase = "revoked_management_replay_failed";
                await cases.RevokedManagementPermissionCannotReplayAnEarlierCommandForAnotherDriver();
                passed.Add("revoked_management_replay");

                if (consolidation)
                {
                    var audit = new AuditMetadataSqlTests(fixture);
                    phase = "large_profile_audit_9_failed";
                    await audit.LargeProfileAudit_PreservesEveryProductAndRollsBackOnAuditFailure(9);
                    passed.Add("large_profile_audit_9");

                    phase = "large_profile_audit_20_failed";
                    await audit.LargeProfileAudit_PreservesEveryProductAndRollsBackOnAuditFailure(20);
                    passed.Add("large_profile_audit_20");

                    phase = "unicode_audit_roundtrip_failed";
                    await audit.ExpandedAuditColumn_RoundTripsUnicodeWithoutTruncation();
                    passed.Add("unicode_audit_roundtrip");

                    phase = "audit_migration_preservation_failed";
                    await audit.AuditMigration_PreservesExistingRowsAndRefusesLossyDowngrade();
                    passed.Add("audit_migration_preservation");
                }
            }
        }
        catch (ValidationFailure failure) { issue = failure.Code; }
        catch (Exception) { issue = phase; }
        finally
        {
            SqlServerFixture.DisposableDatabaseCreating -= TrackDatabase;
            if (fixture is not null && normalizedConnection is not null)
            {
                // Attempt fixture cleanup, then independently prove absence.
                // This also handles a lost CREATE response before its _created flag.
                try { await fixture.DisposeAsync(); }
                catch (Exception) { /* The independent cleanup below is authoritative. */ }
                try
                {
                    cleanupVerified = generatedDatabases.Count > 0;
                    foreach (var database in generatedDatabases)
                    {
                        try { await CleanupGeneratedDatabaseAsync(normalizedConnection, database); }
                        catch (Exception) { cleanupVerified = false; }
                    }
                    if (!cleanupVerified) issue = "isolated_database_cleanup_failed";
                }
                catch (Exception)
                {
                    cleanupVerified = false;
                    issue = "isolated_database_cleanup_failed";
                }
            }
            else if (fixture is null)
            {
                // No connection was opened and no SQL database was created.
                cleanupVerified = true;
            }
            Environment.SetEnvironmentVariable(ConnectionVariable, previousConnection);
            Console.SetOut(output);
            Console.SetError(error);
        }

        var success = issue is null && cleanupVerified && passed.Count == expected;
        foreach (var code in passed) output.WriteLine("PASS " + code);
        if (!success) output.WriteLine("FAIL " + (issue ?? "validation_incomplete"));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            schema = consolidation ? ConsolidationProtocol : Protocol,
            status = success ? "passed" : "failed",
            passed = passed.Count,
            expected,
            cleanupVerified,
            cleanupDatabase = !cleanupVerified && fixture is not null && GeneratedDatabase().IsMatch(fixture.DisposableDatabaseName)
                ? fixture.DisposableDatabaseName : null,
            cleanupDatabases = consolidation && !cleanupVerified ? generatedDatabases.ToArray() : null,
            issue = success ? null : issue ?? "validation_incomplete"
        }, Json));
        return success ? 0 : 1;
    }

    private static string NormalizeConnection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationFailure("connection_missing");
        try
        {
            var connection = new SqlConnectionStringBuilder(value);
            if (string.IsNullOrWhiteSpace(connection.DataSource) || !string.IsNullOrEmpty(connection.AttachDBFilename)
                || connection.UserInstance)
                throw new ValidationFailure("connection_invalid");
            // The caller's catalog is deliberately ignored. Tests create a new
            // generated name; none of the business database names are accepted.
            connection.InitialCatalog = "master";
            connection.ApplicationName = "PRODIMT I22 isolated validation";
            connection.ConnectTimeout = 5;
            connection.PersistSecurityInfo = false;
            return connection.ConnectionString;
        }
        catch (ValidationFailure) { throw; }
        catch (Exception) { throw new ValidationFailure("connection_invalid"); }
    }

    private static void RequirePortableFiles()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "MvcTestingAppManifest.json");
        var apiAssembly = typeof(global::Program).Assembly.GetName();
        if (!File.Exists(manifestPath) || !File.Exists(Path.Combine(AppContext.BaseDirectory, apiAssembly.Name + ".deps.json")))
            throw new ValidationFailure("portable_files_invalid");
        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(manifestPath));
        // MVC.Testing v10.0.8 explicitly treats '~' as the runtime base directory.
        // Publishing its targets generates this manifest, without repository paths.
        if (manifest is null || !manifest.TryGetValue(apiAssembly.FullName, out var root) || root != "~"
            || manifest.Values.Any(path => path != "~"))
            throw new ValidationFailure("portable_content_root_invalid");
    }

    private static async Task VerifyPortableHostAsync()
    {
        // /health does not access a database. If startup accidentally begins
        // using SQL, this deliberately unreachable, fictional connection fails.
        await using var factory = new SqlServerFixture.SqlApiFactory(
            "Server=127.0.0.1,1;Database=PortableSelfTest;User Id=portable;Password=Fictional-self-test-only-2026!;Connect Timeout=1;Encrypt=True;TrustServerCertificate=True");
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/health");
        if (response.StatusCode != HttpStatusCode.OK) throw new ValidationFailure("portable_host_failed");
    }

    private static async Task CleanupGeneratedDatabaseAsync(string masterConnection, string name)
    {
        if (!GeneratedDatabase().IsMatch(name)) throw new ValidationFailure("isolated_database_name_invalid");
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(masterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.Parameters.AddWithValue("@name", name);
        // The identifier is generated by the fixture and strictly checked above.
        command.CommandText = $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END; SELECT DB_ID(@name);";
        var result = await command.ExecuteScalarAsync();
        if (result is not null and not DBNull) throw new ValidationFailure("isolated_database_cleanup_failed");
    }

    [GeneratedRegex("^ProdimtI02Test_[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedDatabase();

    private sealed class ValidationFailure(string code) : Exception
    {
        public string Code { get; } = code;
    }
}
