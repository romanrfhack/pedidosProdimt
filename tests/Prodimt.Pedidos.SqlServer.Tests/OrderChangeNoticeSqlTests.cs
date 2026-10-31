using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class OrderChangeNoticeSqlTests(SqlServerFixture database)
{
    private const string Header = "externalOrderKey,serviceDate,customerExternalCode,action,productExternalCode,quantity,unitCode,notes";

    [Fact]
    public async Task CorrectionNotice_AndAcknowledgementSurviveNewHost_WithoutDuplicatingOrChangingDemand()
    {
        var catalog = await database.CreateCatalogAsync();
        Guid orderId;
        Guid noticeId;
        await using (var factory = database.CreateFactory())
        using (var client = await SqlServerFixture.CreateAdminClientAsync(factory))
        {
            orderId = await ImportAsync(client, catalog);
            var correction = Correction(catalog, 1, 20m, "Synthetic request adds five kg");
            using var corrected = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", correction);
            corrected.EnsureSuccessStatusCode();
            using var replay = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", correction);
            replay.EnsureSuccessStatusCode();
            var notice = Assert.Single(await NoticesAsync(client, orderId));
            noticeId = notice.GetProperty("id").GetGuid();
            Assert.Equal(JsonValueKind.Null, notice.GetProperty("machineId").ValueKind);
            Assert.Equal("correction", notice.GetProperty("kind").GetString());
            Assert.Equal(2, notice.GetProperty("orderVersion").GetInt32());
            var line = Assert.Single(notice.GetProperty("lines").EnumerateArray());
            Assert.Equal("kg", line.GetProperty("unitCode").GetString());
            Assert.Equal(15m, line.GetProperty("beforeQuantity").GetDecimal());
            Assert.Equal(20m, line.GetProperty("afterQuantity").GetDecimal());
            Assert.Equal(5m, line.GetProperty("deltaQuantity").GetDecimal());
        }

        await using var nextFactory = database.CreateFactory();
        using var nextClient = await SqlServerFixture.CreateAdminClientAsync(nextFactory);
        Assert.Equal(noticeId, Assert.Single(await NoticesAsync(nextClient, orderId)).GetProperty("id").GetGuid());
        using var unauthenticated = nextFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var forbiddenList = await unauthenticated.GetAsync("/api/admin/operations/changes?date=2026-10-25");
        Assert.Equal(HttpStatusCode.Unauthorized, forbiddenList.StatusCode);
        var ackRequest = new { operationKey = Guid.NewGuid().ToString("N"), expectedVersion = 1 };
        using var forbiddenAck = await unauthenticated.PostAsJsonAsync($"/api/admin/operations/changes/{noticeId}/acknowledge", ackRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, forbiddenAck.StatusCode);
        using var acknowledged = await nextClient.PostAsJsonAsync($"/api/admin/operations/changes/{noticeId}/acknowledge", ackRequest);
        var ack = await JsonAsync(acknowledged);
        Assert.Equal(2, ack.GetProperty("version").GetInt32());
        Assert.Equal("SQL Test Admin", ack.GetProperty("acknowledgedBy").GetString());
        Assert.NotEqual(JsonValueKind.Null, ack.GetProperty("acknowledgedAt").ValueKind);
        using var ackReplay = await nextClient.PostAsJsonAsync($"/api/admin/operations/changes/{noticeId}/acknowledge", ackRequest);
        Assert.Equal(ack.GetRawText(), (await JsonAsync(ackReplay)).GetRawText());
        Assert.Empty(await NoticesAsync(nextClient, orderId));
        Assert.Single(await NoticesAsync(nextClient, orderId, pendingOnly: false));
        await using (var db = database.CreateDbContext())
        {
            var order = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == orderId);
            Assert.Equal(20m, Assert.Single(order.Lines).Quantity);
            Assert.Equal(2, order.Version);
            Assert.Single(await db.OrderChangeNotices.Where(x => x.OrderId == orderId).ToArrayAsync());
        }

        // A later edit must create a new pending revision even if the earlier one was received.
        using var decrease = await nextClient.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections",
            Correction(catalog, 2, 12m, "Synthetic customer reduces quantity"));
        decrease.EnsureSuccessStatusCode();
        var later = Assert.Single(await NoticesAsync(nextClient, orderId));
        Assert.NotEqual(noticeId, later.GetProperty("id").GetGuid());
        Assert.Equal(3, later.GetProperty("orderVersion").GetInt32());
        Assert.Equal(-8m, later.GetProperty("lines")[0].GetProperty("deltaQuantity").GetDecimal());
    }

    [Fact]
    public async Task ConcurrentAcknowledgements_RecordOneActor_AndLoserCannotReplaySuccess()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var factory = database.CreateFactory();
        using var firstClient = await SqlServerFixture.CreateAdminClientAsync(factory);
        var secondName = "notice-operator-" + Guid.NewGuid().ToString("N");
        const string secondPassword = "Synthetic-notice-operator-2026!";
        await using (var seed = database.CreateDbContext())
        {
            seed.AdminUsers.Add(AdminUser.Create(secondName, "Synthetic second operator",
                new PasswordHashService().HashPassword(secondPassword), SqlServerFixture.Now));
            await seed.SaveChangesAsync();
        }
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var loginResponse = await secondClient.PostAsJsonAsync("/api/auth/admin/login", new { userName = secondName, password = secondPassword });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AdminLoginResponse>();
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        var orderId = await ImportAsync(firstClient, catalog);
        using var corrected = await firstClient.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections",
            Correction(catalog, 1, 20m, "Synthetic concurrent acknowledgement"));
        corrected.EnsureSuccessStatusCode();
        var noticeId = Assert.Single(await NoticesAsync(firstClient, orderId)).GetProperty("id").GetGuid();
        var first = new { operationKey = Guid.NewGuid().ToString("N"), expectedVersion = 1 };
        var second = new { operationKey = Guid.NewGuid().ToString("N"), expectedVersion = 1 };
        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync($"/api/admin/operations/changes/{noticeId}/acknowledge", first),
            secondClient.PostAsJsonAsync($"/api/admin/operations/changes/{noticeId}/acknowledge", second));
        bool firstWon;
        try
        {
            Assert.Single(responses, x => x.IsSuccessStatusCode);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            firstWon = responses[0].IsSuccessStatusCode;
        }
        finally { foreach (var response in responses) response.Dispose(); }

        var loser = firstWon ? second : first;
        var winner = firstWon ? first : second;
        var loserClient = firstWon ? secondClient : firstClient;
        using var loserRetry = await loserClient.PostAsJsonAsync($"/api/admin/operations/changes/{noticeId}/acknowledge", loser);
        Assert.Equal(HttpStatusCode.Conflict, loserRetry.StatusCode);
        var winnerClient = firstWon ? firstClient : secondClient;
        using var winnerReplay = await winnerClient.PostAsJsonAsync($"/api/admin/operations/changes/{noticeId}/acknowledge", winner);
        winnerReplay.EnsureSuccessStatusCode();
        await using var persisted = database.CreateDbContext();
        var notice = await persisted.OrderChangeNotices.SingleAsync(x => x.Id == noticeId);
        Assert.Equal(2, notice.Version);
        Assert.Equal(firstWon ? "SQL Test Admin" : "Synthetic second operator", notice.AcknowledgedBy);
        Assert.NotNull(notice.AcknowledgedAt);
        var loserKey = "NOTICE-ACK:" + loser.operationKey.ToUpperInvariant();
        var winnerKey = "NOTICE-ACK:" + winner.operationKey.ToUpperInvariant();
        Assert.False(await persisted.OrderOperations.AnyAsync(x => x.OperationKey == loserKey));
        Assert.Single(await persisted.OrderOperations.Where(x => x.OperationKey == winnerKey).ToArrayAsync());
        Assert.Equal(20m, (await persisted.OrderLines.SingleAsync(x => x.OrderId == orderId)).Quantity);
    }

    [Fact]
    public async Task NoticeInsertFailure_RollsBackCorrectionAuditAndReceipt_ThenSameKeyRetries()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var factory = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(factory);
        var orderId = await ImportAsync(client, catalog);
        var correction = Correction(catalog, 1, 20m, "Synthetic failed notice insertion");
        var constraint = $"CK_I04NoticeTest_{Guid.NewGuid():N}";
        await ExecuteGeneratedTestDdlAsync($"ALTER TABLE [OrderChangeNotices] ADD CONSTRAINT [{constraint}] CHECK ([OrderId] <> '{orderId}')");
        try
        {
            HttpResponseMessage? failed = null;
            Exception? failure = null;
            try { failed = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", correction); }
            catch (Exception error) { failure = error; }
            using (failed) Assert.True(failure is not null ? IsConstraintFailure(failure) : failed?.StatusCode == HttpStatusCode.InternalServerError);
            await using var verification = database.CreateDbContext();
            var order = await verification.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == orderId);
            Assert.Equal(1, order.Version);
            Assert.Equal(15m, Assert.Single(order.Lines).Quantity);
            Assert.False(await verification.OrderChangeNotices.AnyAsync(x => x.OrderId == orderId));
            Assert.False(await verification.OrderAuditLogs.AnyAsync(x => x.OrderId == orderId && x.EventType == OrderAuditEventType.OrderCorrected));
            var receiptKey = "CORRECTION:" + correction.operationKey.ToUpperInvariant();
            Assert.False(await verification.OrderOperations.AnyAsync(x => x.OperationKey == receiptKey));
        }
        finally { await ExecuteGeneratedTestDdlAsync($"ALTER TABLE [OrderChangeNotices] DROP CONSTRAINT [{constraint}]"); }

        using var retry = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", correction);
        retry.EnsureSuccessStatusCode();
        using var replay = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", correction);
        replay.EnsureSuccessStatusCode();
        await using var persisted = database.CreateDbContext();
        Assert.Single(await persisted.OrderChangeNotices.Where(x => x.OrderId == orderId).ToArrayAsync());
        Assert.Single(await persisted.OrderAuditLogs.Where(x => x.OrderId == orderId && x.EventType == OrderAuditEventType.OrderCorrected).ToArrayAsync());
        Assert.Equal(20m, (await persisted.OrderLines.SingleAsync(x => x.OrderId == orderId)).Quantity);
    }

    [Fact]
    public async Task PendingCorrection_CreatesNoticeOnlyWhenAccepted_AndReviewRetryDoesNotDuplicate()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var factory = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(factory);
        var orderId = await ImportAsync(client, catalog, "2026-10-23");
        using var corrected = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections",
            Correction(catalog, 1, 20m, "Synthetic pending correction"));
        corrected.EnsureSuccessStatusCode();
        Assert.Empty(await NoticesAsync(client, orderId, date: "2026-10-23"));
        var review = new { decision = "Accepted", expectedVersion = 2, internalNotes = "Synthetic pending approval" };
        using var accepted = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/review", review);
        accepted.EnsureSuccessStatusCode();
        var notice = Assert.Single(await NoticesAsync(client, orderId, date: "2026-10-23"));
        Assert.Equal("review", notice.GetProperty("kind").GetString());
        Assert.Equal(3, notice.GetProperty("orderVersion").GetInt32());
        Assert.Equal(0m, notice.GetProperty("lines")[0].GetProperty("beforeQuantity").GetDecimal());
        Assert.Equal(20m, notice.GetProperty("lines")[0].GetProperty("afterQuantity").GetDecimal());
        using var staleReview = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/review", review);
        Assert.Equal(HttpStatusCode.Conflict, staleReview.StatusCode);
        Assert.Single(await NoticesAsync(client, orderId, date: "2026-10-23"));
    }

    [Fact]
    public async Task NoticeInsertFailure_RollsBackReviewDecisionAndAudit_ThenSameVersionRetries()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var factory = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(factory);
        var orderId = await ImportAsync(client, catalog, "2026-10-23");
        var review = new { decision = "Accepted", expectedVersion = 1, internalNotes = "Synthetic approval with durable notice" };
        var constraint = $"CK_I04ReviewNoticeTest_{Guid.NewGuid():N}";
        await ExecuteGeneratedTestDdlAsync($"ALTER TABLE [OrderChangeNotices] ADD CONSTRAINT [{constraint}] CHECK ([OrderId] <> '{orderId}')");
        try
        {
            HttpResponseMessage? failed = null;
            Exception? failure = null;
            try { failed = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/review", review); }
            catch (Exception error) { failure = error; }
            using (failed) Assert.True(failure is not null ? IsConstraintFailure(failure) : failed?.StatusCode == HttpStatusCode.InternalServerError);
            await using var verification = database.CreateDbContext();
            var order = await verification.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == orderId);
            Assert.Equal(OrderStatus.PendingAdminReview, order.Status);
            Assert.Equal(AdminDecision.Pending, order.AdminDecision);
            Assert.True(order.RequiresAdminReview);
            Assert.Equal(1, order.Version);
            Assert.NotEqual(review.internalNotes, order.InternalNotes);
            Assert.Equal(15m, Assert.Single(order.Lines).Quantity);
            Assert.False(await verification.OrderChangeNotices.AnyAsync(x => x.OrderId == orderId));
            Assert.False(await verification.OrderAuditLogs.AnyAsync(x => x.OrderId == orderId && x.EventType == OrderAuditEventType.AdminDecisionRecorded));
        }
        finally { await ExecuteGeneratedTestDdlAsync($"ALTER TABLE [OrderChangeNotices] DROP CONSTRAINT [{constraint}]"); }

        using var retry = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/review", review);
        retry.EnsureSuccessStatusCode();
        using var staleRetry = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/review", review);
        Assert.Equal(HttpStatusCode.Conflict, staleRetry.StatusCode);
        await using var persisted = database.CreateDbContext();
        var accepted = await persisted.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(OrderStatus.Accepted, accepted.Status);
        Assert.Equal(2, accepted.Version);
        Assert.Equal(review.internalNotes, accepted.InternalNotes);
        Assert.Single(await persisted.OrderChangeNotices.Where(x => x.OrderId == orderId).ToArrayAsync());
        Assert.Single(await persisted.OrderAuditLogs.Where(x => x.OrderId == orderId && x.EventType == OrderAuditEventType.AdminDecisionRecorded).ToArrayAsync());
        var notice = Assert.Single(await NoticesAsync(client, orderId, date: "2026-10-23"));
        Assert.Equal(0m, notice.GetProperty("lines")[0].GetProperty("beforeQuantity").GetDecimal());
        Assert.Equal(15m, notice.GetProperty("lines")[0].GetProperty("afterQuantity").GetDecimal());
    }

    private static CorrectionBody Correction(SqlServerFixture.TestCatalog catalog, int version, decimal quantity, string reason)
        => new(Guid.NewGuid().ToString("N"), version, reason, [new(catalog.ProductId, quantity, null)]);

    private static async Task<Guid> ImportAsync(HttpClient client, SqlServerFixture.TestCatalog catalog, string date = "2026-10-25")
    {
        using var response = await client.PostAsJsonAsync("/api/admin/daily-orders/apply", new
        {
            content = $"{Header}\norder,{date},{catalog.CustomerCode},order,{catalog.ProductCode},15,kg,",
            source = Guid.NewGuid().ToString("N"), batchKey = Guid.NewGuid().ToString("N"), fileName = "notice-test.csv"
        });
        return (await JsonAsync(response)).GetProperty("orders")[0].GetProperty("orderId").GetGuid();
    }

    private static async Task<JsonElement[]> NoticesAsync(HttpClient client, Guid orderId, bool pendingOnly = true, string date = "2026-10-25")
    {
        using var response = await client.GetAsync($"/api/admin/operations/changes?date={date}&pendingOnly={pendingOnly.ToString().ToLowerInvariant()}");
        return (await JsonAsync(response)).GetProperty("changes").EnumerateArray()
            .Where(x => x.GetProperty("orderId").GetGuid() == orderId).Select(x => x.Clone()).ToArray();
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private async Task ExecuteGeneratedTestDdlAsync(string text)
    {
        // These identifiers contain only locally generated GUIDs in the disposable fixture DB.
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = text;
        await command.ExecuteNonQueryAsync();
    }

    private static bool IsConstraintFailure(Exception exception) => exception is SqlException { Number: 547 } ||
        exception.InnerException is not null && IsConstraintFailure(exception.InnerException);

    private sealed record CorrectionBody(string operationKey, int expectedVersion, string reason, CorrectionLine[] lines);
    private sealed record CorrectionLine(Guid productId, decimal quantity, string? notes);
}
