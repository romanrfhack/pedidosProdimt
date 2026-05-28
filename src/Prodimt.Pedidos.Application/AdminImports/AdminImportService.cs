using System.Globalization;
using System.Text;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminImports;

public sealed class AdminImportService(
    CsvImportParser parser,
    ICustomerRepository customers,
    IProductRepository products,
    IMachineRepository machines,
    IAuditLogRepository auditLogs,
    IDateTimeProvider dateTimeProvider)
{
    public const int MaxFileSizeBytes = 2 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, TemplateDefinition> Templates =
        new Dictionary<string, TemplateDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [AdminImportTypes.Customers] = new(
                AdminImportTypes.Customers,
                "Clientes externos para piloto.",
                "docs/import-templates/customers.csv",
                "docs/import-templates/examples/customers-demo.csv",
                [
                    new("externalCode", false, "Codigo externo opcional para relacionar datos depurados."),
                    new("name", true, "Nombre comercial del cliente."),
                    new("phoneNumber", false, "Telefono principal."),
                    new("isActive", false, "true/false, 1/0 o si/no. Default true."),
                    new("preferredDeliveryTime", false, "Hora deseada en formato HH:mm."),
                    new("preferredDeliveryWindowStart", false, "Inicio de ventana en formato HH:mm."),
                    new("preferredDeliveryWindowEnd", false, "Fin de ventana en formato HH:mm."),
                    new("deliveryNotes", false, "Notas de entrega.")
                ]),
            [AdminImportTypes.Products] = new(
                AdminImportTypes.Products,
                "Productos o moldes.",
                "docs/import-templates/products.csv",
                "docs/import-templates/examples/products-demo.csv",
                [
                    new("externalCode", false, "Codigo externo opcional."),
                    new("name", true, "Nombre del producto o molde."),
                    new("description", false, "Descripcion interna opcional."),
                    new("isActive", false, "true/false, 1/0 o si/no. Default true.")
                ]),
            [AdminImportTypes.CustomerFrequentProducts] = new(
                AdminImportTypes.CustomerFrequentProducts,
                "Productos frecuentes por cliente. Reemplaza solo clientes presentes en el archivo.",
                "docs/import-templates/customer-frequent-products.csv",
                "docs/import-templates/examples/customer-frequent-products-demo.csv",
                [
                    new("customerExternalCode", false, "Codigo externo del cliente."),
                    new("customerName", false, "Nombre del cliente si no hay codigo externo."),
                    new("productExternalCode", false, "Codigo externo del producto."),
                    new("productName", false, "Nombre del producto si no hay codigo externo."),
                    new("defaultQuantity", false, "Cantidad sugerida en formato invariante."),
                    new("sortOrder", false, "Orden. Si viene vacio o <= 0 se normaliza."),
                    new("isActive", false, "true/false, 1/0 o si/no. Default true.")
                ]),
            [AdminImportTypes.Machines] = new(
                AdminImportTypes.Machines,
                "Maquinas internas.",
                "docs/import-templates/machines.csv",
                "docs/import-templates/examples/machines-demo.csv",
                [
                    new("externalCode", false, "Codigo externo opcional."),
                    new("number", true, "Numero interno de maquina."),
                    new("name", false, "Nombre opcional."),
                    new("isActive", false, "true/false, 1/0 o si/no. Default true.")
                ]),
            [AdminImportTypes.CustomerMachineAssignments] = new(
                AdminImportTypes.CustomerMachineAssignments,
                "Asignaciones internas cliente-maquina. Reemplaza solo clientes presentes en el archivo.",
                "docs/import-templates/customer-machine-assignments.csv",
                "docs/import-templates/examples/customer-machine-assignments-demo.csv",
                [
                    new("customerExternalCode", false, "Codigo externo del cliente."),
                    new("customerName", false, "Nombre del cliente si no hay codigo externo."),
                    new("machineExternalCode", false, "Codigo externo de la maquina."),
                    new("machineNumber", false, "Numero de maquina si no hay codigo externo."),
                    new("isDefault", false, "true/false, 1/0 o si/no. Default false."),
                    new("notes", false, "Notas internas.")
                ])
        };

    public ImportTemplatesResponse GetTemplates()
    {
        return new ImportTemplatesResponse(
            MaxFileSizeBytes,
            "stateless-validate-then-apply",
            Templates.Values
                .Select(template => new ImportTemplateResponse(
                    template.ImportType,
                    template.Description,
                    template.Columns,
                    template.TemplatePath,
                    template.ExamplePath))
                .ToArray());
    }

    public async Task<ImportValidationResponse> ValidateAsync(
        string importType,
        ImportCsvRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await BuildPlanAsync(importType, request, cancellationToken);
        return plan.Response;
    }

    public async Task<ImportApplyResponse> ApplyAsync(
        string importType,
        ImportCsvRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var plan = await BuildPlanAsync(importType, request, cancellationToken);
        if (plan.Response.Errors.Count > 0)
        {
            return new ImportApplyResponse(
                plan.ImportType,
                plan.Response.TotalRows,
                CreatedCount: 0,
                UpdatedCount: 0,
                SkippedCount: plan.Response.TotalRows,
                plan.Response.WarningCount,
                AuditLogIds: [],
                plan.Response.Errors);
        }

        return plan switch
        {
            ImportPlan<CustomerImportItem> customerPlan => await ApplyCustomersAsync(customerPlan, actor, cancellationToken),
            ImportPlan<ProductImportItem> productPlan => await ApplyProductsAsync(productPlan, actor, cancellationToken),
            ImportPlan<MachineImportItem> machinePlan => await ApplyMachinesAsync(machinePlan, actor, cancellationToken),
            ImportPlan<FrequentProductImportItem> frequentPlan => await ApplyFrequentProductsAsync(frequentPlan, actor, cancellationToken),
            ImportPlan<MachineAssignmentImportItem> assignmentPlan => await ApplyMachineAssignmentsAsync(assignmentPlan, actor, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported import plan.")
        };
    }

    private Task<IImportPlan> BuildPlanAsync(
        string importType,
        ImportCsvRequest request,
        CancellationToken cancellationToken)
    {
        if (!Templates.TryGetValue(importType, out var template))
        {
            throw new ArgumentException($"Tipo de importacion no soportado: {importType}.", nameof(importType));
        }

        return template.ImportType switch
        {
            AdminImportTypes.Customers => BuildCustomerPlanAsync(template, request, cancellationToken),
            AdminImportTypes.Products => BuildProductPlanAsync(template, request, cancellationToken),
            AdminImportTypes.Machines => BuildMachinePlanAsync(template, request, cancellationToken),
            AdminImportTypes.CustomerFrequentProducts => BuildFrequentProductPlanAsync(template, request, cancellationToken),
            AdminImportTypes.CustomerMachineAssignments => BuildMachineAssignmentPlanAsync(template, request, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported import template.")
        };
    }

    private async Task<IImportPlan> BuildCustomerPlanAsync(
        TemplateDefinition template,
        ImportCsvRequest request,
        CancellationToken cancellationToken)
    {
        var context = CreateParseContext(template, request);
        var items = new List<CustomerImportItem>();
        var proposedChanges = new List<ImportProposedChangeDto>();

        if (context.HasBlockingHeaderErrors)
        {
            return CreatePlan(template.ImportType, context, items, proposedChanges);
        }

        var allCustomers = await customers.GetAllAsync(cancellationToken);
        var byExternalCode = BuildLookup(allCustomers, x => x.ExternalCode, NormalizeExternalCode);
        var byName = BuildLookup(allCustomers, x => x.Name, NormalizeName);
        var seenKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in context.Rows)
        {
            var errorsBefore = context.Errors.Count;
            var externalCode = NullIfWhiteSpace(row.Get("externalCode"));
            var name = row.Get("name").Trim();
            var phoneNumber = NullIfWhiteSpace(row.Get("phoneNumber"));
            var isActive = ParseBooleanField(row, "isActive", defaultValue: true, context);
            var preferredDeliveryTime = ParseTimeField(row, "preferredDeliveryTime", context);
            var preferredDeliveryWindowStart = ParseTimeField(row, "preferredDeliveryWindowStart", context);
            var preferredDeliveryWindowEnd = ParseTimeField(row, "preferredDeliveryWindowEnd", context);
            var deliveryNotes = NullIfWhiteSpace(row.Get("deliveryNotes"));

            if (string.IsNullOrWhiteSpace(name))
            {
                context.Errors.Add(Required(row, "name"));
            }

            TrackDuplicate(
                seenKeys,
                BuildCustomerFileKey(externalCode, name),
                row,
                "name",
                "DuplicateCustomer",
                "Cliente duplicado en el mismo archivo.",
                context);
            TrackPossibleDuplicateName(seenNames, externalCode, name, row, context, "CustomerPossibleDuplicate");

            if (externalCode is null)
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "externalCode",
                    "NameFallbackMatching",
                    "No se encontro externalCode; se usara nombre normalizado para matching.",
                    null));
            }

            if (phoneNumber is null)
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "phoneNumber",
                    "EmptyPhoneNumber",
                    "Telefono vacio.",
                    null));
            }

            if (context.Errors.Count != errorsBefore)
            {
                continue;
            }

            var matchedByNameFallback = false;
            var existing = FindByExternalOrName(byExternalCode, byName, externalCode, name, out matchedByNameFallback);
            if (matchedByNameFallback && externalCode is not null)
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "externalCode",
                    "ExternalCodeWillBeAssigned",
                    "No existe el externalCode, pero el nombre coincide; se actualizara el registro y se asignara el codigo.",
                    externalCode));
            }

            var action = DetermineCatalogAction(existing?.IsActive, isActive);
            if (existing is not null)
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "name",
                    "CustomerWillBeUpdated",
                    "El cliente ya existe y sera actualizado.",
                    name));
            }

            items.Add(new CustomerImportItem(
                row.RowNumber,
                externalCode,
                name,
                phoneNumber,
                isActive,
                preferredDeliveryTime,
                preferredDeliveryWindowStart,
                preferredDeliveryWindowEnd,
                deliveryNotes,
                existing?.Id,
                action));
            proposedChanges.Add(new ImportProposedChangeDto(
                row.RowNumber,
                action,
                "Customer",
                existing?.Id.ToString(),
                name,
                existing is null ? "Crear cliente." : $"Actualizar cliente {existing.Name}."));
        }

        return CreatePlan(template.ImportType, context, items, proposedChanges);
    }

    private async Task<IImportPlan> BuildProductPlanAsync(
        TemplateDefinition template,
        ImportCsvRequest request,
        CancellationToken cancellationToken)
    {
        var context = CreateParseContext(template, request);
        var items = new List<ProductImportItem>();
        var proposedChanges = new List<ImportProposedChangeDto>();

        if (context.HasBlockingHeaderErrors)
        {
            return CreatePlan(template.ImportType, context, items, proposedChanges);
        }

        var allProducts = await products.GetAllAsync(cancellationToken);
        var byExternalCode = BuildLookup(allProducts, x => x.ExternalCode, NormalizeExternalCode);
        var byName = BuildLookup(allProducts, x => x.Name, NormalizeName);
        var seenKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in context.Rows)
        {
            var errorsBefore = context.Errors.Count;
            var externalCode = NullIfWhiteSpace(row.Get("externalCode"));
            var name = row.Get("name").Trim();
            var description = NullIfWhiteSpace(row.Get("description"));
            var isActive = ParseBooleanField(row, "isActive", defaultValue: true, context);

            if (string.IsNullOrWhiteSpace(name))
            {
                context.Errors.Add(Required(row, "name"));
            }

            TrackDuplicate(
                seenKeys,
                BuildNameBackedFileKey(externalCode, name),
                row,
                "name",
                "DuplicateProduct",
                "Producto duplicado en el mismo archivo.",
                context);
            TrackPossibleDuplicateName(seenNames, externalCode, name, row, context, "ProductPossibleDuplicate");

            if (externalCode is null)
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "externalCode",
                    "NameFallbackMatching",
                    "No se encontro externalCode; se usara nombre normalizado para matching.",
                    null));
            }

            if (context.Errors.Count != errorsBefore)
            {
                continue;
            }

            var matchedByNameFallback = false;
            var existing = FindByExternalOrName(byExternalCode, byName, externalCode, name, out matchedByNameFallback);
            if (matchedByNameFallback && externalCode is not null)
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "externalCode",
                    "ExternalCodeWillBeAssigned",
                    "No existe el externalCode, pero el nombre coincide; se actualizara el registro y se asignara el codigo.",
                    externalCode));
            }

            var action = DetermineCatalogAction(existing?.IsActive, isActive);
            if (existing is not null)
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "name",
                    "ProductWillBeUpdated",
                    "El producto ya existe y sera actualizado.",
                    name));
            }

            items.Add(new ProductImportItem(
                row.RowNumber,
                externalCode,
                name,
                description,
                isActive,
                existing?.Id,
                action));
            proposedChanges.Add(new ImportProposedChangeDto(
                row.RowNumber,
                action,
                "Product",
                existing?.Id.ToString(),
                name,
                existing is null ? "Crear producto." : $"Actualizar producto {existing.Name}."));
        }

        return CreatePlan(template.ImportType, context, items, proposedChanges);
    }

    private async Task<IImportPlan> BuildMachinePlanAsync(
        TemplateDefinition template,
        ImportCsvRequest request,
        CancellationToken cancellationToken)
    {
        var context = CreateParseContext(template, request);
        var items = new List<MachineImportItem>();
        var proposedChanges = new List<ImportProposedChangeDto>();

        if (context.HasBlockingHeaderErrors)
        {
            return CreatePlan(template.ImportType, context, items, proposedChanges);
        }

        var allMachines = await machines.GetAllAsync(cancellationToken);
        var byExternalCode = BuildLookup(allMachines, x => x.ExternalCode, NormalizeExternalCode);
        var byNumber = allMachines
            .GroupBy(x => x.Number)
            .Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single());
        var seenKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in context.Rows)
        {
            var errorsBefore = context.Errors.Count;
            var externalCode = NullIfWhiteSpace(row.Get("externalCode"));
            var number = ParseRequiredPositiveInt(row, "number", context);
            var name = NullIfWhiteSpace(row.Get("name"));
            var isActive = ParseBooleanField(row, "isActive", defaultValue: true, context);

            TrackDuplicate(
                seenKeys,
                externalCode is null ? $"number:{number}" : $"external:{NormalizeExternalCode(externalCode)}",
                row,
                "number",
                "DuplicateMachine",
                "Maquina duplicada en el mismo archivo.",
                context);

            if (externalCode is null)
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "externalCode",
                    "NumberFallbackMatching",
                    "No se encontro externalCode; se usara numero de maquina para matching.",
                    null));
            }

            if (context.Errors.Count != errorsBefore)
            {
                continue;
            }

            var matchedByNumberFallback = false;
            var existing = FindMachine(byExternalCode, byNumber, externalCode, number, out matchedByNumberFallback);
            if (matchedByNumberFallback && externalCode is not null)
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "externalCode",
                    "ExternalCodeWillBeAssigned",
                    "No existe el externalCode, pero el numero coincide; se actualizara el registro y se asignara el codigo.",
                    externalCode));
            }

            var action = DetermineCatalogAction(existing?.IsActive, isActive);
            if (existing is not null)
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "number",
                    "MachineWillBeUpdated",
                    "La maquina ya existe y sera actualizada.",
                    number.ToString(CultureInfo.InvariantCulture)));
            }

            items.Add(new MachineImportItem(
                row.RowNumber,
                externalCode,
                number,
                name,
                isActive,
                existing?.Id,
                action));
            proposedChanges.Add(new ImportProposedChangeDto(
                row.RowNumber,
                action,
                "Machine",
                existing?.Id.ToString(),
                $"#{number}",
                existing is null ? "Crear maquina." : $"Actualizar maquina #{existing.Number}."));
        }

        return CreatePlan(template.ImportType, context, items, proposedChanges);
    }

    private async Task<IImportPlan> BuildFrequentProductPlanAsync(
        TemplateDefinition template,
        ImportCsvRequest request,
        CancellationToken cancellationToken)
    {
        var context = CreateParseContext(template, request);
        var items = new List<FrequentProductImportItem>();
        var proposedChanges = new List<ImportProposedChangeDto>();

        if (context.HasBlockingHeaderErrors)
        {
            return CreatePlan(template.ImportType, context, items, proposedChanges);
        }

        var allCustomers = await customers.GetAllAsync(cancellationToken);
        var allProducts = await products.GetAllAsync(cancellationToken);
        var customerByExternalCode = BuildLookup(allCustomers, x => x.ExternalCode, NormalizeExternalCode);
        var customerByName = BuildLookup(allCustomers, x => x.Name, NormalizeName);
        var productByExternalCode = BuildLookup(allProducts, x => x.ExternalCode, NormalizeExternalCode);
        var productByName = BuildLookup(allProducts, x => x.Name, NormalizeName);
        var seenPairs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in context.Rows)
        {
            var errorsBefore = context.Errors.Count;
            var customerExternalCode = NullIfWhiteSpace(row.Get("customerExternalCode"));
            var customerName = NullIfWhiteSpace(row.Get("customerName"));
            var productExternalCode = NullIfWhiteSpace(row.Get("productExternalCode"));
            var productName = NullIfWhiteSpace(row.Get("productName"));
            var defaultQuantity = ParseOptionalDecimal(row, "defaultQuantity", context);
            var sortOrder = ParseOptionalInt(row, "sortOrder", context);
            var isActive = ParseBooleanField(row, "isActive", defaultValue: true, context);

            if (defaultQuantity < 0)
            {
                context.Errors.Add(new ImportIssueDto(
                    row.RowNumber,
                    "defaultQuantity",
                    "NegativeQuantity",
                    "La cantidad default no puede ser negativa.",
                    row.Get("defaultQuantity")));
            }

            if (sortOrder <= 0 && !string.IsNullOrWhiteSpace(row.Get("sortOrder")))
            {
                context.Warnings.Add(new ImportIssueDto(
                    row.RowNumber,
                    "sortOrder",
                    "SortOrderWillBeNormalized",
                    "El orden debe ser positivo; se normalizara por posicion.",
                    row.Get("sortOrder")));
            }

            var customer = FindByExternalOrName(
                customerByExternalCode,
                customerByName,
                customerExternalCode,
                customerName,
                out _);
            if (customer is null)
            {
                context.Errors.Add(new ImportIssueDto(
                    row.RowNumber,
                    "customerExternalCode",
                    "CustomerNotFound",
                    "Cliente requerido no encontrado.",
                    customerExternalCode ?? customerName));
            }

            var product = FindByExternalOrName(
                productByExternalCode,
                productByName,
                productExternalCode,
                productName,
                out _);
            if (product is null)
            {
                context.Errors.Add(new ImportIssueDto(
                    row.RowNumber,
                    "productExternalCode",
                    "ProductNotFound",
                    "Producto requerido no encontrado.",
                    productExternalCode ?? productName));
            }

            if (customer is not null && product is not null)
            {
                TrackDuplicate(
                    seenPairs,
                    $"{customer.Id:N}:{product.Id:N}",
                    row,
                    "productExternalCode",
                    "DuplicateFrequentProduct",
                    "Producto frecuente duplicado para el mismo cliente en el mismo archivo.",
                    context);
            }

            if (context.Errors.Count != errorsBefore)
            {
                continue;
            }

            items.Add(new FrequentProductImportItem(
                row.RowNumber,
                customer!.Id,
                customer.Name,
                product!.Id,
                product.Name,
                defaultQuantity,
                sortOrder,
                isActive));
            proposedChanges.Add(new ImportProposedChangeDto(
                row.RowNumber,
                "Replace",
                "CustomerFrequentProduct",
                customer.Id.ToString(),
                $"{customer.Name} / {product.Name}",
                "Reemplazar configuracion de productos frecuentes para el cliente."));
        }

        await AddReplacementWarningsAsync(
            items.Select(x => x.CustomerId).Distinct(),
            async customerId => (await customers.GetAllFrequentProductsAsync(customerId, cancellationToken)).Count,
            "FrequentProductsWillReplace",
            "Producto frecuente reemplazara configuracion previa del cliente.",
            context);

        return CreatePlan(template.ImportType, context, items, proposedChanges);
    }

    private async Task<IImportPlan> BuildMachineAssignmentPlanAsync(
        TemplateDefinition template,
        ImportCsvRequest request,
        CancellationToken cancellationToken)
    {
        var context = CreateParseContext(template, request);
        var items = new List<MachineAssignmentImportItem>();
        var proposedChanges = new List<ImportProposedChangeDto>();

        if (context.HasBlockingHeaderErrors)
        {
            return CreatePlan(template.ImportType, context, items, proposedChanges);
        }

        var allCustomers = await customers.GetAllAsync(cancellationToken);
        var allMachines = await machines.GetAllAsync(cancellationToken);
        var customerByExternalCode = BuildLookup(allCustomers, x => x.ExternalCode, NormalizeExternalCode);
        var customerByName = BuildLookup(allCustomers, x => x.Name, NormalizeName);
        var machineByExternalCode = BuildLookup(allMachines, x => x.ExternalCode, NormalizeExternalCode);
        var machineByNumber = allMachines
            .GroupBy(x => x.Number)
            .Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single());
        var seenPairs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in context.Rows)
        {
            var errorsBefore = context.Errors.Count;
            var customerExternalCode = NullIfWhiteSpace(row.Get("customerExternalCode"));
            var customerName = NullIfWhiteSpace(row.Get("customerName"));
            var machineExternalCode = NullIfWhiteSpace(row.Get("machineExternalCode"));
            var machineNumber = ParseOptionalInt(row, "machineNumber", context);
            var isDefault = ParseBooleanField(row, "isDefault", defaultValue: false, context);
            var notes = NullIfWhiteSpace(row.Get("notes"));

            var customer = FindByExternalOrName(
                customerByExternalCode,
                customerByName,
                customerExternalCode,
                customerName,
                out _);
            if (customer is null)
            {
                context.Errors.Add(new ImportIssueDto(
                    row.RowNumber,
                    "customerExternalCode",
                    "CustomerNotFound",
                    "Cliente requerido no encontrado.",
                    customerExternalCode ?? customerName));
            }

            Machine? machine = null;
            if (machineExternalCode is not null &&
                machineByExternalCode.TryGetValue(NormalizeExternalCode(machineExternalCode), out var externalMachine))
            {
                machine = externalMachine;
            }
            else if (machineNumber is > 0 && machineByNumber.TryGetValue(machineNumber.Value, out var numberMachine))
            {
                machine = numberMachine;
            }

            if (machine is null)
            {
                context.Errors.Add(new ImportIssueDto(
                    row.RowNumber,
                    "machineExternalCode",
                    "MachineNotFound",
                    "Maquina requerida no encontrada.",
                    machineExternalCode ?? row.Get("machineNumber")));
            }
            else if (isDefault && !machine.IsActive)
            {
                context.Errors.Add(new ImportIssueDto(
                    row.RowNumber,
                    "isDefault",
                    "InactiveMachineCannotBeDefault",
                    "Una maquina inactiva no puede ser default.",
                    row.Get("isDefault")));
            }

            if (customer is not null && machine is not null)
            {
                TrackDuplicate(
                    seenPairs,
                    $"{customer.Id:N}:{machine.Id:N}",
                    row,
                    "machineExternalCode",
                    "DuplicateMachineAssignment",
                    "Asignacion de maquina duplicada para el mismo cliente en el mismo archivo.",
                    context);
            }

            if (context.Errors.Count != errorsBefore)
            {
                continue;
            }

            items.Add(new MachineAssignmentImportItem(
                row.RowNumber,
                customer!.Id,
                customer.Name,
                machine!.Id,
                machine.Number,
                machine.Name,
                isDefault,
                notes));
            proposedChanges.Add(new ImportProposedChangeDto(
                row.RowNumber,
                "Replace",
                "CustomerMachineAssignment",
                customer.Id.ToString(),
                $"{customer.Name} / #{machine.Number}",
                "Reemplazar asignaciones internas de maquina para el cliente."));
        }

        foreach (var group in items.GroupBy(x => x.CustomerId))
        {
            if (group.Count(x => x.IsDefault) > 1)
            {
                foreach (var item in group.Where(x => x.IsDefault).Skip(1))
                {
                    context.Errors.Add(new ImportIssueDto(
                        item.RowNumber,
                        "isDefault",
                        "MultipleDefaultMachines",
                        "Solo puede existir una maquina default por cliente.",
                        "true"));
                }
            }
        }

        await AddReplacementWarningsAsync(
            items.Select(x => x.CustomerId).Distinct(),
            async customerId => (await customers.GetAllMachineAssignmentsAsync(customerId, cancellationToken)).Count,
            "MachineAssignmentsWillReplace",
            "Asignacion de maquina reemplazara configuracion previa del cliente.",
            context);

        return CreatePlan(template.ImportType, context, items, proposedChanges);
    }

    private async Task<ImportApplyResponse> ApplyCustomersAsync(
        ImportPlan<CustomerImportItem> plan,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var auditLogIds = new List<Guid>();
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var now = dateTimeProvider.Now;

        foreach (var item in plan.Items)
        {
            Customer? customer;
            string eventType;

            if (item.ExistingId is null)
            {
                customer = Customer.Create(
                    item.Name,
                    item.PhoneNumber,
                    item.PreferredDeliveryTime,
                    item.PreferredDeliveryWindowStart,
                    item.PreferredDeliveryWindowEnd,
                    item.DeliveryNotes,
                    now);
                if (item.ExternalCode is not null)
                {
                    customer.SetExternalCode(item.ExternalCode, now);
                }

                if (!item.IsActive)
                {
                    customer.Deactivate(now);
                }

                await customers.AddAsync(customer, cancellationToken);
                created++;
                eventType = CatalogAuditEventTypes.CustomerImportedCreated;
            }
            else
            {
                customer = await customers.GetByIdForUpdateAsync(item.ExistingId.Value, cancellationToken);
                if (customer is null)
                {
                    skipped++;
                    continue;
                }

                customer.Update(
                    item.Name,
                    item.PhoneNumber,
                    item.PreferredDeliveryTime,
                    item.PreferredDeliveryWindowStart,
                    item.PreferredDeliveryWindowEnd,
                    item.DeliveryNotes,
                    now);
                if (item.ExternalCode is not null)
                {
                    customer.SetExternalCode(item.ExternalCode, now);
                }

                if (item.IsActive)
                {
                    customer.Activate(now);
                }
                else
                {
                    customer.Deactivate(now);
                }

                updated++;
                eventType = CatalogAuditEventTypes.CustomerImportedUpdated;
            }

            await AddAuditAsync(
                auditLogIds,
                CatalogEntityTypes.Customer,
                customer.Id,
                eventType,
                $"Cliente importado: {customer.Name}.",
                new { plan.ImportType, item.RowNumber, item.ExternalCode },
                actor,
                cancellationToken);
        }

        await AddBulkAuditAsync(auditLogIds, plan, created, updated, skipped, actor, cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);
        return ToApplyResponse(plan, created, updated, skipped, auditLogIds);
    }

    private async Task<ImportApplyResponse> ApplyProductsAsync(
        ImportPlan<ProductImportItem> plan,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var auditLogIds = new List<Guid>();
        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var item in plan.Items)
        {
            Product? product;
            string eventType;

            if (item.ExistingId is null)
            {
                product = Product.Create(item.Name, item.Description);
                if (item.ExternalCode is not null)
                {
                    product.SetExternalCode(item.ExternalCode);
                }

                if (!item.IsActive)
                {
                    product.Deactivate();
                }

                await products.AddAsync(product, cancellationToken);
                created++;
                eventType = CatalogAuditEventTypes.ProductImportedCreated;
            }
            else
            {
                product = await products.GetByIdForUpdateAsync(item.ExistingId.Value, cancellationToken);
                if (product is null)
                {
                    skipped++;
                    continue;
                }

                product.Update(item.Name, item.Description);
                if (item.ExternalCode is not null)
                {
                    product.SetExternalCode(item.ExternalCode);
                }

                if (item.IsActive)
                {
                    product.Activate();
                }
                else
                {
                    product.Deactivate();
                }

                updated++;
                eventType = CatalogAuditEventTypes.ProductImportedUpdated;
            }

            await AddAuditAsync(
                auditLogIds,
                CatalogEntityTypes.Product,
                product.Id,
                eventType,
                $"Producto importado: {product.Name}.",
                new { plan.ImportType, item.RowNumber, item.ExternalCode },
                actor,
                cancellationToken);
        }

        await AddBulkAuditAsync(auditLogIds, plan, created, updated, skipped, actor, cancellationToken);
        await products.SaveChangesAsync(cancellationToken);
        return ToApplyResponse(plan, created, updated, skipped, auditLogIds);
    }

    private async Task<ImportApplyResponse> ApplyMachinesAsync(
        ImportPlan<MachineImportItem> plan,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var auditLogIds = new List<Guid>();
        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var item in plan.Items)
        {
            Machine? machine;
            string eventType;

            if (item.ExistingId is null)
            {
                machine = Machine.Create(item.Number, item.Name);
                if (item.ExternalCode is not null)
                {
                    machine.SetExternalCode(item.ExternalCode);
                }

                if (!item.IsActive)
                {
                    machine.Deactivate();
                }

                await machines.AddAsync(machine, cancellationToken);
                created++;
                eventType = CatalogAuditEventTypes.MachineImportedCreated;
            }
            else
            {
                machine = await machines.GetByIdForUpdateAsync(item.ExistingId.Value, cancellationToken);
                if (machine is null)
                {
                    skipped++;
                    continue;
                }

                machine.Update(item.Number, item.Name);
                if (item.ExternalCode is not null)
                {
                    machine.SetExternalCode(item.ExternalCode);
                }

                if (item.IsActive)
                {
                    machine.Activate();
                }
                else
                {
                    machine.Deactivate();
                }

                updated++;
                eventType = CatalogAuditEventTypes.MachineImportedUpdated;
            }

            await AddAuditAsync(
                auditLogIds,
                CatalogEntityTypes.Machine,
                machine.Id,
                eventType,
                $"Maquina importada: #{machine.Number}.",
                new { plan.ImportType, item.RowNumber, item.ExternalCode },
                actor,
                cancellationToken);
        }

        await AddBulkAuditAsync(auditLogIds, plan, created, updated, skipped, actor, cancellationToken);
        await machines.SaveChangesAsync(cancellationToken);
        return ToApplyResponse(plan, created, updated, skipped, auditLogIds);
    }

    private async Task<ImportApplyResponse> ApplyFrequentProductsAsync(
        ImportPlan<FrequentProductImportItem> plan,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var auditLogIds = new List<Guid>();
        var updated = 0;

        foreach (var group in plan.Items.GroupBy(x => x.CustomerId))
        {
            var replacement = group
                .Select((item, index) => new CustomerFrequentProduct
                {
                    CustomerId = item.CustomerId,
                    ProductId = item.ProductId,
                    DefaultQuantity = item.DefaultQuantity,
                    SortOrder = item.SortOrder is > 0 ? item.SortOrder.Value : index + 1,
                    IsActive = item.IsActive
                })
                .OrderBy(x => x.SortOrder)
                .ToArray();

            await customers.ReplaceFrequentProductsAsync(group.Key, replacement, cancellationToken);
            updated++;

            await AddAuditAsync(
                auditLogIds,
                CatalogEntityTypes.Customer,
                group.Key,
                CatalogAuditEventTypes.CustomerFrequentProductsImported,
                $"Productos frecuentes importados para {group.First().CustomerName}.",
                new
                {
                    plan.ImportType,
                    count = replacement.Length,
                    productIds = replacement.Select(x => x.ProductId).ToArray()
                },
                actor,
                cancellationToken);
        }

        await AddBulkAuditAsync(auditLogIds, plan, created: 0, updated, skipped: 0, actor, cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);
        return ToApplyResponse(plan, created: 0, updated, skipped: 0, auditLogIds);
    }

    private async Task<ImportApplyResponse> ApplyMachineAssignmentsAsync(
        ImportPlan<MachineAssignmentImportItem> plan,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var auditLogIds = new List<Guid>();
        var updated = 0;

        foreach (var group in plan.Items.GroupBy(x => x.CustomerId))
        {
            var replacement = group
                .Select(item => new CustomerMachineAssignment
                {
                    CustomerId = item.CustomerId,
                    MachineId = item.MachineId,
                    IsDefault = item.IsDefault,
                    IsActive = true,
                    Notes = item.Notes
                })
                .ToArray();

            await customers.ReplaceMachineAssignmentsAsync(group.Key, replacement, cancellationToken);
            updated++;

            await AddAuditAsync(
                auditLogIds,
                CatalogEntityTypes.Customer,
                group.Key,
                CatalogAuditEventTypes.CustomerMachineAssignmentsImported,
                $"Asignaciones internas de maquina importadas para {group.First().CustomerName}.",
                new
                {
                    plan.ImportType,
                    count = replacement.Length,
                    defaultMachineId = replacement.FirstOrDefault(x => x.IsDefault)?.MachineId,
                    machineIds = replacement.Select(x => x.MachineId).ToArray()
                },
                actor,
                cancellationToken);
        }

        await AddBulkAuditAsync(auditLogIds, plan, created: 0, updated, skipped: 0, actor, cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);
        return ToApplyResponse(plan, created: 0, updated, skipped: 0, auditLogIds);
    }

    private ImportParseContext CreateParseContext(TemplateDefinition template, ImportCsvRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ImportIssueDto>();
        var warnings = new List<ImportIssueDto>();
        var content = request.Content ?? string.Empty;

        if (Encoding.UTF8.GetByteCount(content) > MaxFileSizeBytes)
        {
            errors.Add(new ImportIssueDto(
                0,
                null,
                "FileTooLarge",
                $"El archivo CSV excede el limite de {MaxFileSizeBytes} bytes.",
                request.FileName));
            return new ImportParseContext(template, [], errors, warnings, HasBlockingHeaderErrors: true);
        }

        var parsed = parser.Parse(content);
        ValidateHeaders(template, parsed.Headers, errors, warnings);

        return new ImportParseContext(
            template,
            parsed.Rows.Where(x => !x.IsEmpty).ToArray(),
            errors,
            warnings,
            HasBlockingHeaderErrors: errors.Any(x => x.RowNumber <= 1));
    }

    private static void ValidateHeaders(
        TemplateDefinition template,
        IReadOnlyList<string> headers,
        ICollection<ImportIssueDto> errors,
        ICollection<ImportIssueDto> warnings)
    {
        if (headers.Count == 0)
        {
            errors.Add(new ImportIssueDto(1, null, "MissingHeader", "El CSV debe incluir encabezados.", null));
            return;
        }

        var expected = template.Columns.Select(x => x.Name).ToArray();
        var headerSet = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);

        foreach (var expectedHeader in expected)
        {
            if (!headerSet.Contains(expectedHeader))
            {
                errors.Add(new ImportIssueDto(
                    1,
                    expectedHeader,
                    "MissingHeader",
                    $"Falta el encabezado requerido: {expectedHeader}.",
                    null));
            }
        }

        foreach (var duplicate in headers
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key))
        {
            errors.Add(new ImportIssueDto(
                1,
                duplicate,
                "DuplicateHeader",
                $"Encabezado duplicado: {duplicate}.",
                duplicate));
        }

        foreach (var extraHeader in headers.Where(x => !expected.Contains(x, StringComparer.OrdinalIgnoreCase)))
        {
            warnings.Add(new ImportIssueDto(
                1,
                extraHeader,
                "UnexpectedHeader",
                $"Encabezado no esperado sera ignorado: {extraHeader}.",
                extraHeader));
        }
    }

    private async Task AddReplacementWarningsAsync(
        IEnumerable<Guid> customerIds,
        Func<Guid, Task<int>> existingCountResolver,
        string code,
        string message,
        ImportParseContext context)
    {
        foreach (var customerId in customerIds)
        {
            var existingCount = await existingCountResolver(customerId);
            if (existingCount <= 0)
            {
                continue;
            }

            var firstRow = context.Rows.FirstOrDefault(row =>
                row.Get("customerExternalCode").Length > 0 ||
                row.Get("customerName").Length > 0);
            context.Warnings.Add(new ImportIssueDto(firstRow?.RowNumber ?? 0, null, code, message, null));
        }
    }

    private static ImportPlan<T> CreatePlan<T>(
        string importType,
        ImportParseContext context,
        IReadOnlyList<T> items,
        IReadOnlyList<ImportProposedChangeDto> proposedChanges)
    {
        var errors = context.Errors.ToArray();
        var warnings = context.Warnings.ToArray();
        var rowErrorNumbers = errors
            .Where(x => x.RowNumber > 1)
            .Select(x => x.RowNumber)
            .ToHashSet();
        var validRows = errors.Any(x => x.RowNumber <= 1)
            ? 0
            : Math.Max(0, context.Rows.Count - rowErrorNumbers.Count);

        var filteredProposedChanges = errors.Length == 0
            ? proposedChanges
            : proposedChanges
                .Where(change => !rowErrorNumbers.Contains(change.RowNumber))
                .ToArray();

        return new ImportPlan<T>(
            importType,
            items,
            new ImportValidationResponse(
                importType,
                context.Rows.Count,
                validRows,
                errors.Length,
                warnings.Length,
                filteredProposedChanges.Count(x => x.Action == "Create"),
                filteredProposedChanges.Count(x => x.Action is "Update" or "Replace"),
                filteredProposedChanges.Count(x => x.Action == "Deactivate"),
                errors,
                warnings,
                filteredProposedChanges));
    }

    private static ImportIssueDto Required(CsvImportRow row, string field)
    {
        return new ImportIssueDto(
            row.RowNumber,
            field,
            "Required",
            $"Campo requerido vacio: {field}.",
            row.Get(field));
    }

    private static bool ParseBooleanField(
        CsvImportRow row,
        string field,
        bool defaultValue,
        ImportParseContext context)
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        var normalized = NormalizeName(raw);
        if (normalized is "TRUE" or "1" or "SI" or "YES")
        {
            return true;
        }

        if (normalized is "FALSE" or "0" or "NO")
        {
            return false;
        }

        context.Errors.Add(new ImportIssueDto(
            row.RowNumber,
            field,
            "InvalidBoolean",
            "Booleano invalido. Usa true/false, 1/0 o si/no.",
            raw));
        return defaultValue;
    }

    private static TimeOnly? ParseTimeField(CsvImportRow row, string field, ImportParseContext context)
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (TimeOnly.TryParseExact(raw.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        context.Errors.Add(new ImportIssueDto(
            row.RowNumber,
            field,
            "InvalidTime",
            "Hora invalida. Usa formato HH:mm.",
            raw));
        return null;
    }

    private static decimal? ParseOptionalDecimal(CsvImportRow row, string field, ImportParseContext context)
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        context.Errors.Add(new ImportIssueDto(
            row.RowNumber,
            field,
            "InvalidDecimal",
            "Decimal invalido. Usa formato invariante, por ejemplo 10.5.",
            raw));
        return null;
    }

    private static int? ParseOptionalInt(CsvImportRow row, string field, ImportParseContext context)
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        context.Errors.Add(new ImportIssueDto(
            row.RowNumber,
            field,
            "InvalidInteger",
            "Entero invalido.",
            raw));
        return null;
    }

    private static int ParseRequiredPositiveInt(CsvImportRow row, string field, ImportParseContext context)
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            context.Errors.Add(Required(row, field));
            return 0;
        }

        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            context.Errors.Add(new ImportIssueDto(row.RowNumber, field, "InvalidInteger", "Entero invalido.", raw));
            return 0;
        }

        if (value <= 0)
        {
            context.Errors.Add(new ImportIssueDto(
                row.RowNumber,
                field,
                "InvalidPositiveInteger",
                "El numero debe ser mayor a cero.",
                raw));
            return 0;
        }

        return value;
    }

    private static void TrackDuplicate(
        IDictionary<string, int> seenKeys,
        string? key,
        CsvImportRow row,
        string field,
        string code,
        string message,
        ImportParseContext context)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (seenKeys.TryGetValue(key, out var firstRowNumber))
        {
            context.Errors.Add(new ImportIssueDto(
                row.RowNumber,
                field,
                code,
                $"{message} Primera aparicion en fila {firstRowNumber}.",
                row.Get(field)));
            return;
        }

        seenKeys[key] = row.RowNumber;
    }

    private static void TrackPossibleDuplicateName(
        IDictionary<string, string> seenNames,
        string? externalCode,
        string name,
        CsvImportRow row,
        ImportParseContext context,
        string code)
    {
        var normalizedName = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return;
        }

        var normalizedExternalCode = externalCode is null ? string.Empty : NormalizeExternalCode(externalCode);
        if (seenNames.TryGetValue(normalizedName, out var firstExternalCode) &&
            !string.Equals(firstExternalCode, normalizedExternalCode, StringComparison.OrdinalIgnoreCase))
        {
            context.Warnings.Add(new ImportIssueDto(
                row.RowNumber,
                "name",
                code,
                "Hay nombres normalizados repetidos con codigos externos diferentes; revisar posible duplicado.",
                name));
            return;
        }

        seenNames[normalizedName] = normalizedExternalCode;
    }

    private static string BuildCustomerFileKey(string? externalCode, string name)
    {
        return BuildNameBackedFileKey(externalCode, name);
    }

    private static string BuildNameBackedFileKey(string? externalCode, string name)
    {
        return externalCode is not null
            ? $"external:{NormalizeExternalCode(externalCode)}"
            : $"name:{NormalizeName(name)}";
    }

    private static Dictionary<string, T> BuildLookup<T>(
        IEnumerable<T> items,
        Func<T, string?> keySelector,
        Func<string, string> normalizer)
    {
        return items
            .Select(item => new { Item = item, Key = NullIfWhiteSpace(keySelector(item)) })
            .Where(x => x.Key is not null)
            .GroupBy(x => normalizer(x.Key!), StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single().Item, StringComparer.OrdinalIgnoreCase);
    }

    private static T? FindByExternalOrName<T>(
        IReadOnlyDictionary<string, T> byExternalCode,
        IReadOnlyDictionary<string, T> byName,
        string? externalCode,
        string? name,
        out bool matchedByNameFallback)
    {
        matchedByNameFallback = false;

        if (externalCode is not null &&
            byExternalCode.TryGetValue(NormalizeExternalCode(externalCode), out var byCode))
        {
            return byCode;
        }

        if (!string.IsNullOrWhiteSpace(name) &&
            byName.TryGetValue(NormalizeName(name), out var byNormalizedName))
        {
            matchedByNameFallback = externalCode is not null;
            return byNormalizedName;
        }

        return default;
    }

    private static Machine? FindMachine(
        IReadOnlyDictionary<string, Machine> byExternalCode,
        IReadOnlyDictionary<int, Machine> byNumber,
        string? externalCode,
        int number,
        out bool matchedByNumberFallback)
    {
        matchedByNumberFallback = false;

        if (externalCode is not null &&
            byExternalCode.TryGetValue(NormalizeExternalCode(externalCode), out var byCode))
        {
            return byCode;
        }

        if (number > 0 && byNumber.TryGetValue(number, out var byMachineNumber))
        {
            matchedByNumberFallback = externalCode is not null;
            return byMachineNumber;
        }

        return null;
    }

    private static string DetermineCatalogAction(bool? existingIsActive, bool incomingIsActive)
    {
        if (existingIsActive is null)
        {
            return "Create";
        }

        return existingIsActive.Value && !incomingIsActive ? "Deactivate" : "Update";
    }

    private async Task AddAuditAsync(
        ICollection<Guid> auditLogIds,
        string entityType,
        Guid entityId,
        string eventType,
        string summary,
        object? metadata,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var auditLog = CatalogAudit.Create(
            entityType,
            entityId,
            eventType,
            dateTimeProvider.Now,
            actor,
            summary,
            metadata);
        auditLogIds.Add(auditLog.Id);
        await auditLogs.AddAsync(auditLog, cancellationToken);
    }

    private async Task AddBulkAuditAsync<T>(
        ICollection<Guid> auditLogIds,
        ImportPlan<T> plan,
        int created,
        int updated,
        int skipped,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var auditLog = CatalogAudit.Create(
            CatalogEntityTypes.BulkImport,
            Guid.NewGuid(),
            CatalogAuditEventTypes.BulkImportApplied,
            dateTimeProvider.Now,
            actor,
            $"Importacion aplicada: {plan.ImportType}.",
            new
            {
                plan.ImportType,
                plan.Response.TotalRows,
                created,
                updated,
                skipped,
                warnings = plan.Response.WarningCount
            });
        auditLogIds.Add(auditLog.Id);
        await auditLogs.AddAsync(auditLog, cancellationToken);
    }

    private static ImportApplyResponse ToApplyResponse<T>(
        ImportPlan<T> plan,
        int created,
        int updated,
        int skipped,
        IReadOnlyList<Guid> auditLogIds)
    {
        return new ImportApplyResponse(
            plan.ImportType,
            plan.Response.TotalRows,
            created,
            updated,
            skipped,
            plan.Response.WarningCount,
            auditLogIds,
            Errors: []);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeExternalCode(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return string.Join(
            " ",
            builder.ToString()
                .Normalize(NormalizationForm.FormC)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private sealed record TemplateDefinition(
        string ImportType,
        string Description,
        string TemplatePath,
        string ExamplePath,
        IReadOnlyList<ImportColumnDto> Columns);

    private sealed record ImportParseContext(
        TemplateDefinition Template,
        IReadOnlyList<CsvImportRow> Rows,
        List<ImportIssueDto> Errors,
        List<ImportIssueDto> Warnings,
        bool HasBlockingHeaderErrors);

    private interface IImportPlan
    {
        string ImportType { get; }

        ImportValidationResponse Response { get; }
    }

    private sealed record ImportPlan<T>(
        string ImportType,
        IReadOnlyList<T> Items,
        ImportValidationResponse Response) : IImportPlan;

    private sealed record CustomerImportItem(
        int RowNumber,
        string? ExternalCode,
        string Name,
        string? PhoneNumber,
        bool IsActive,
        TimeOnly? PreferredDeliveryTime,
        TimeOnly? PreferredDeliveryWindowStart,
        TimeOnly? PreferredDeliveryWindowEnd,
        string? DeliveryNotes,
        Guid? ExistingId,
        string Action);

    private sealed record ProductImportItem(
        int RowNumber,
        string? ExternalCode,
        string Name,
        string? Description,
        bool IsActive,
        Guid? ExistingId,
        string Action);

    private sealed record MachineImportItem(
        int RowNumber,
        string? ExternalCode,
        int Number,
        string? Name,
        bool IsActive,
        Guid? ExistingId,
        string Action);

    private sealed record FrequentProductImportItem(
        int RowNumber,
        Guid CustomerId,
        string CustomerName,
        Guid ProductId,
        string ProductName,
        decimal? DefaultQuantity,
        int? SortOrder,
        bool IsActive);

    private sealed record MachineAssignmentImportItem(
        int RowNumber,
        Guid CustomerId,
        string CustomerName,
        Guid MachineId,
        int MachineNumber,
        string? MachineName,
        bool IsDefault,
        string? Notes);
}
