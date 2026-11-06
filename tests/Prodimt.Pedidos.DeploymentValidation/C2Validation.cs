using System.Runtime.InteropServices;
using System.Text.Json;
using Prodimt.Pedidos.C2.Tests;

namespace Prodimt.Pedidos.DeploymentValidation;

internal static class C2Validation
{
    public static async Task<int> Probe()
    {
        var output = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = "127.0.0.1,15555", InitialCatalog = "C2_00000000000000000000000000000000",
                UserID = "c2_probe", Password = C2Database.Password, Encrypt = true, TrustServerCertificate = true
            }.ConnectionString;
            await using var host = new C2Api(connection, new C2Clock(), null);
            using var client = host.Client();
            var response = await client.GetAsync("/health");
            response.EnsureSuccessStatusCode();
            if (client.BaseAddress!.Host != "127.0.0.1") throw new InvalidOperationException();
            output.WriteLine("{\"status\":\"passed\",\"scope\":\"Kestrel HTTP health only; no SQL assertions\"}");
            return 0;
        }
        catch (Exception e)
        {
            output.WriteLine(JsonSerializer.Serialize(new { status = "failed", scope = "Kestrel HTTP health only",
                errorType = e.GetType().Name, innerType = e.InnerException?.GetType().Name,
                issue = e.Message.StartsWith("c2_", StringComparison.Ordinal) ? e.Message : "host_initialization_or_request_failed",
                socketError = (e as System.Net.Sockets.SocketException ?? e.InnerException as System.Net.Sockets.SocketException)?.SocketErrorCode.ToString() }));
            return 1;
        }
        finally { Console.SetOut(output); }
    }

    public static async Task<int> Run(string mode, string file)
    {
        var output = Console.Out; var error = Console.Error;
        var results = new List<object>(); C2Database? database = null;
        var cleanup = true; string? issue = null; C2Configuration? config = null;
        Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null);
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            config = C2Configuration.Read(file);
            database = new C2Database(config); cleanup = false;
            await database.InitializeAsync();
            if (mode == "--validate-c2")
            {
                foreach (var name in C2Scenarios.Names)
                {
                    try { await C2Scenarios.Run(name, database); results.Add(new { name, status = "passed" }); }
                    catch (Exception e)
                    {
                        results.Add(new { name, status = "failed", errorType = e.GetType().Name,
                            testFrame = e.StackTrace?.Split('\n').FirstOrDefault(line => line.Contains("C2Scenarios.", StringComparison.Ordinal))?.Trim() });
                        issue = "c2_cases_failed";
                    }
                    // Never print exception messages, SQL, credentials, tokens or HTTP bodies.
                }
            }
            else if (mode == "--serve-c2")
            {
                await using var host = database.Host();
                using var probe = host.Client();
                (await probe.GetAsync("/health/db")).EnsureSuccessStatusCode();
                var scene = await C2Browser.Prepare(database, host);
                var readyFile = Path.Combine(config.Directory, "browser-private.json");
                await File.WriteAllTextAsync(readyFile, JsonSerializer.Serialize(new
                {
                    baseUrl = probe.BaseAddress!.GetLeftPart(UriPartial.Authority), date = "2026-11-04",
                    scene.Portal, scene.Delivery, scene.TripId, scene.AssignmentId, scene.OrderId,
                    adminName = "c2-admin", password = C2Database.Password
                }, C2Json.Options));
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(readyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                using var cancelled = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                using var registration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; cancelled.Cancel(); });
                ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancelled.Cancel(); };
                Console.CancelKeyPress += handler;
                try
                {
                    while (!File.Exists(Path.Combine(config.Directory, "browser-finished")))
                        await Task.Delay(200, cancelled.Token);
                    var snapshot = await C2Browser.Verify(database, scene);
                    await File.WriteAllTextAsync(Path.Combine(config.Directory, "sql-browser-verification.json"), JsonSerializer.Serialize(snapshot, C2Json.Options));
                    results.Add(new { name = "browser_independent_sql", status = "passed" });
                }
                finally { Console.CancelKeyPress -= handler; }
            }
            else throw new InvalidOperationException("c2_mode_invalid");
        }
        catch (Exception e) { issue = "c2_infrastructure_or_verification_failed:" + e.GetType().Name; }
        finally
        {
            if (database is not null)
            {
                try { await database.DisposeAsync(); cleanup = database.CleanupVerified; }
                catch (Exception) { cleanup = false; issue = "c2_cleanup_failed"; }
            }
            Console.SetOut(output); Console.SetError(error);
        }
        var expected = mode == "--validate-c2" ? C2Scenarios.Names.Length : 1;
        var success = issue is null && cleanup && results.Count == expected;
        var report = JsonSerializer.Serialize(new
        {
            schema = "prodimt.c2.http-sql.v1", mode, status = success ? "passed" : "failed",
            expected, results, cleanupVerified = cleanup, recoveryPending = !cleanup,
            database = database?.Name, transport = "Kestrel HTTP loopback", persistence = "SQL Server", issue
        }, C2Json.Options);
        output.WriteLine(report);
        if (config is not null) await File.WriteAllTextAsync(Path.Combine(config.Directory,
            mode == "--validate-c2" ? "http-sql.json" : "browser-host.json"), report);
        return success ? 0 : 1;
    }
}
