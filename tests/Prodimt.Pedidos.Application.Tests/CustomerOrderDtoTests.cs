using Prodimt.Pedidos.Application.CustomerOrders;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class CustomerOrderDtoTests
{
    [Fact]
    public void CustomerFacingOrderDtos_DoNotExposeAssignedMachine()
    {
        var customerDtoPropertyNames = typeof(CustomerOrderTodayResponse)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();
        var orderResponsePropertyNames = typeof(CustomerOrderResponse)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();
        var currentOrderSummaryPropertyNames = typeof(CustomerCurrentOrderSummaryResponse)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();
        var productSuggestionPropertyNames = typeof(ProductSuggestionDto)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();

        Assert.DoesNotContain(customerDtoPropertyNames, ContainsMachineName);
        Assert.DoesNotContain(orderResponsePropertyNames, ContainsMachineName);
        Assert.DoesNotContain(currentOrderSummaryPropertyNames, ContainsMachineName);
        Assert.DoesNotContain(productSuggestionPropertyNames, ContainsMachineName);
        Assert.DoesNotContain(customerDtoPropertyNames, ContainsAuditName);
        Assert.DoesNotContain(orderResponsePropertyNames, ContainsAuditName);
        Assert.DoesNotContain(currentOrderSummaryPropertyNames, ContainsAuditName);
        Assert.DoesNotContain(productSuggestionPropertyNames, ContainsAuditName);
    }

    private static bool ContainsMachineName(string propertyName)
    {
        return propertyName.Contains("Machine", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("AssignedMachine", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAuditName(string propertyName)
    {
        return propertyName.Contains("Audit", StringComparison.OrdinalIgnoreCase);
    }
}
