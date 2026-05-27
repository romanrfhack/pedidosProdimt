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
        var productSuggestionPropertyNames = typeof(ProductSuggestionDto)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();

        Assert.DoesNotContain(customerDtoPropertyNames, ContainsMachineName);
        Assert.DoesNotContain(orderResponsePropertyNames, ContainsMachineName);
        Assert.DoesNotContain(productSuggestionPropertyNames, ContainsMachineName);
    }

    private static bool ContainsMachineName(string propertyName)
    {
        return propertyName.Contains("Machine", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("AssignedMachine", StringComparison.OrdinalIgnoreCase);
    }
}
