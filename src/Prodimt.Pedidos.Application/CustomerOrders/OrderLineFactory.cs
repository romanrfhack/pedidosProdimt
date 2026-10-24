using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.CustomerOrders;

internal sealed record OrderLineInput(Guid ProductId, decimal Quantity, string? Notes);

internal static class OrderLineFactory
{
    public static async Task<OrderLine[]> CreateAsync(
        Guid customerId,
        IReadOnlyList<OrderLineInput>? requestLines,
        ICustomerRepository customers,
        IProductRepository products,
        IMachineRepository machines,
        CancellationToken cancellationToken)
    {
        if (requestLines is null || requestLines.Count == 0)
            throw new ArgumentException("Captura al menos una cantidad o usa No pedir hoy.");
        if (requestLines.Any(x => x.Quantity < 0))
            throw new ArgumentOutOfRangeException(nameof(requestLines), "Las cantidades no pueden ser negativas.");
        if (requestLines.Any(x => decimal.Round(x.Quantity, 2) != x.Quantity || x.Quantity > 9999999999999999.99m))
            throw new ArgumentOutOfRangeException(nameof(requestLines), "Las cantidades deben tener hasta dos decimales y estar dentro del rango permitido.");

        var positiveLines = requestLines.Where(x => x.Quantity > 0).ToArray();
        if (positiveLines.Length == 0)
            throw new ArgumentException("Captura al menos una cantidad o usa No pedir hoy.");
        if (positiveLines.Select(x => x.ProductId).Distinct().Count() != positiveLines.Length)
            throw new ArgumentException("El producto se repite en el pedido; captura una sola cantidad por producto.");
        if (positiveLines.Any(x => x.Notes?.Length > 500))
            throw new ArgumentException("La nota de una linea no puede exceder 500 caracteres.");

        var productLookup = (await products.GetByIdsAsync(positiveLines.Select(x => x.ProductId), cancellationToken)).ToDictionary(x => x.Id);
        var preferences = await customers.GetFrequentProductsAsync(customerId, cancellationToken);
        var assignments = await customers.GetMachineAssignmentsAsync(customerId, cancellationToken);
        var defaultMachine = assignments.FirstOrDefault(x => x.IsActive && x.IsDefault)?.MachineId;
        var candidates = positiveLines.Select(line => new
        {
            Input = line,
            MachineId = preferences.FirstOrDefault(x => x.ProductId == line.ProductId && x.IsActive)?.PreferredMachineId ?? defaultMachine
        }).ToArray();
        var machineLookup = (await machines.GetByIdsAsync(candidates.Select(x => x.MachineId).OfType<Guid>(), cancellationToken)).ToDictionary(x => x.Id);

        return candidates.Select(candidate =>
        {
            var input = candidate.Input;
            if (!productLookup.TryGetValue(input.ProductId, out var product) || !product.IsActive)
                throw new ArgumentException("El producto no existe o esta inactivo.");
            if (product.UnitCode is null || !ProductUnitCodes.IsSupported(product.UnitCode))
                throw new ArgumentException($"Define la unidad del producto {product.Name} antes de capturar un pedido.");
            if (candidate.MachineId is { } machineId && (!machineLookup.TryGetValue(machineId, out var machine) || !machine.IsActive))
                throw new ArgumentException("La maquina asignada no existe o esta inactiva; revisa el perfil del cliente.");

            return new OrderLine
            {
                Id = Guid.NewGuid(), ProductId = product.Id,
                ProductNameSnapshot = product.Name, UnitCode = product.UnitCode,
                Quantity = input.Quantity, AssignedMachineId = candidate.MachineId, Notes = input.Notes
            };
        }).ToArray();
    }
}
