using Prodimt.Pedidos.Api;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class CollectionsPermissionTests
{
    private const string CustomerId = "11111111-1111-1111-1111-111111111111";

    [Theory]
    [InlineData("GET", "/api/admin/collections/cashier", "operations.collections.read")]
    [InlineData("HEAD", "/api/admin/collections/cashier", "operations.collections.read")]
    [InlineData("GET", "/api/admin/collections/customers/" + CustomerId, "operations.collections.read")]
    [InlineData("GET", "/api/admin/collections/trips/" + CustomerId, "operations.collections.read")]
    [InlineData("POST", "/api/admin/collections/receipts", "operations.collections.receive")]
    [InlineData("PUT", "/api/admin/collections/receipts", "__unmapped__")]
    [InlineData("DELETE", "/api/admin/collections/receipts", "__unmapped__")]
    [InlineData("POST", "/api/admin/collections/cashier", "__unmapped__")]
    [InlineData("POST", "/api/admin/collections/customers/" + CustomerId, "__unmapped__")]
    [InlineData("GET", "/api/admin/collections/customers/not-a-guid", "__unmapped__")]
    [InlineData("GET", "/api/admin/collections/trips/not-a-guid", "__unmapped__")]
    [InlineData("POST", "/api/admin/collections/trips/" + CustomerId, "__unmapped__")]
    [InlineData("GET", "/api/admin/collections/unknown", "__unmapped__")]
    public void CollectionsRoutesRequireExplicitCapabilities(string method, string path, string expected)
        => Assert.Equal(expected, AdminPermissionMiddleware.RequiredPermission(method, path));

    [Fact]
    public void ReceiptWritesRequireReceiveInsteadOfReadOrDriverWork()
    {
        Assert.Equal(["operations.collections.receive"],
            AdminPermissionMiddleware.AcceptedPermissions("POST", "/api/admin/collections/receipts"));
        var readers = AdminPermissionMiddleware.AcceptedPermissions("GET", "/api/admin/collections/cashier");
        Assert.Contains("operations.collections.read", readers);
        Assert.Contains("operations.collections.receive", readers);
        Assert.DoesNotContain("operations.collections.work", readers);
        Assert.DoesNotContain("orders.capture", readers);
        Assert.DoesNotContain("delivery.manage", readers);
    }
}
