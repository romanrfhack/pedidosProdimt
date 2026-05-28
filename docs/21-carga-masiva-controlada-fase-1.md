# 21 — Carga masiva controlada Fase 1

Fecha: 2026-05-28

## Objetivo

Preparar datos reales de piloto sin importar ciegamente el Excel operativo ni convertir el `.xlsm` en fuente principal.

La fuente de verdad sigue siendo SQL Server. El Excel se usa solo como referencia para preparar CSV depurados.

## Decision

Se implementa importacion por CSV controlado con flujo `dry-run`/`apply`.

Motivos:

- Permite limpiar datos antes de guardarlos.
- Evita depender de macros, formulas o formatos ambiguos del `.xlsm`.
- Hace visibles errores, advertencias y cambios propuestos antes de modificar catalogos.
- Permite probar con datos demo sin agregar informacion sensible al repositorio.

## Plantillas

Plantillas vacias:

- `docs/import-templates/customers.csv`
- `docs/import-templates/products.csv`
- `docs/import-templates/customer-frequent-products.csv`
- `docs/import-templates/machines.csv`
- `docs/import-templates/customer-machine-assignments.csv`

Ejemplos demo:

- `docs/import-templates/examples/customers-demo.csv`
- `docs/import-templates/examples/products-demo.csv`
- `docs/import-templates/examples/customer-frequent-products-demo.csv`
- `docs/import-templates/examples/machines-demo.csv`
- `docs/import-templates/examples/customer-machine-assignments-demo.csv`

No hay plantilla de tokens planos. Los tokens de cliente se crean desde el sistema despues de importar clientes; el token plano se muestra una sola vez y en base solo se guarda hash.

## Encabezados

Clientes:

```csv
externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes
```

Productos:

```csv
externalCode,name,description,isActive
```

Productos frecuentes:

```csv
customerExternalCode,customerName,productExternalCode,productName,defaultQuantity,sortOrder,isActive
```

Maquinas:

```csv
externalCode,number,name,isActive
```

Asignaciones cliente-maquina:

```csv
customerExternalCode,customerName,machineExternalCode,machineNumber,isDefault,notes
```

## Matching

Se agregaron campos nullable:

- `Customer.ExternalCode`
- `Product.ExternalCode`
- `Machine.ExternalCode`

Reglas:

- Clientes y productos: primero `externalCode`; si no existe o no coincide, nombre normalizado.
- Maquinas: primero `externalCode`; si no existe o no coincide, numero.
- Si falta `externalCode`, se genera advertencia porque el matching por nombre es mas fragil.

## Validaciones

Generales:

- Encabezados esperados obligatorios.
- Filas completamente vacias ignoradas.
- Campos con trim.
- CSV con comillas y comas soportado.
- Booleanos aceptados: `true/false`, `1/0`, `si/no`, `sí/no`.
- Horas en `HH:mm`.
- Decimales en formato invariante.
- Limite inicial: 2 MB por CSV.

Errores bloqueantes:

- Nombre requerido vacio.
- Cliente, producto o maquina requeridos no encontrados.
- Cantidad negativa.
- Duplicado en el mismo archivo.
- Encabezados faltantes o duplicados.
- Hora invalida.
- Mas de una maquina default por cliente.
- Maquina inactiva marcada como default.

Advertencias:

- Registro existente sera actualizado.
- Telefono vacio.
- Falta `externalCode` y se usara nombre o numero.
- Posible duplicado por nombre normalizado.
- Configuracion de productos frecuentes o maquinas reemplazara la existente para clientes presentes.

## Aplicacion

`validate` no modifica base.

`apply` es stateless:

1. Recibe el mismo contenido CSV.
2. Vuelve a validar.
3. Si hay errores bloqueantes, no guarda.
4. Si solo hay advertencias, aplica.
5. Registra auditoria.

Productos frecuentes y asignaciones reemplazan la configuracion completa solo de clientes presentes en el archivo. No borran configuraciones de clientes no incluidos.

## Endpoints

Todos requieren `AdminAccess`:

- `GET /api/admin/import/templates`
- `POST /api/admin/import/{importType}/validate`
- `POST /api/admin/import/{importType}/apply`

Body:

```json
{
  "content": "externalCode,name,...",
  "fileName": "customers.csv"
}
```

Tipos:

- `customers`
- `products`
- `customer-frequent-products`
- `machines`
- `customer-machine-assignments`

Un JWT de cliente recibe `403`.

## UI Angular

Ruta:

```text
/admin/importacion
```

Permite:

- Seleccionar tipo de importacion.
- Cargar archivo CSV o pegar contenido.
- Validar.
- Ver totales, errores, advertencias y cambios propuestos.
- Aplicar solo si no hay errores.

La ruta no aparece para clientes y queda protegida por guard admin.

## Auditoria

La aplicacion registra en `AuditLogs`:

- `BulkImportApplied`
- Eventos por entidad o grupo importado cuando aplica:
  - `CustomerImportedCreated`
  - `CustomerImportedUpdated`
  - `ProductImportedCreated`
  - `ProductImportedUpdated`
  - `MachineImportedCreated`
  - `MachineImportedUpdated`
  - `CustomerFrequentProductsImported`
  - `CustomerMachineAssignmentsImported`

`MetadataJson` guarda resumen controlado: tipo, filas, conteos y ids relevantes. No guarda secretos ni tokens planos.

## Preparar datos desde Excel

1. Trabajar sobre una copia depurada del Excel operativo, nunca sobre el archivo real como fuente directa.
2. Separar clientes externos de `Mostrador`.
3. Normalizar nombres de clientes y moldes.
4. Asignar `externalCode` estables cuando sea posible.
5. Convertir `x/X` a estado operativo `NoOrder` solo en flujos de pedidos, no como cantidad.
6. Pasar columna de maquina a `machines.csv` y `customer-machine-assignments.csv`.
7. Revisar horarios en `HH:mm`.
8. Ejecutar `validate`, corregir errores, repetir.
9. Aplicar solo cuando administracion confirme los cambios propuestos.

## Limitaciones

- No importa directamente `.xlsm`.
- No interpreta macros ni formulas.
- No importa historico de pedidos.
- No importa tokens planos.
- No genera WhatsApp ni mensajes.
- No ejecuta jobs automaticos.
- No implementa produccion por maquina, embarques, repartidores, estadisticas, pagos ni facturacion.

## Pendiente futuro

- Evaluar importacion directa del `.xlsm` solo si se definen reglas completas para hojas, formulas, historico y datos sensibles.
- Agregar exportacion de resultados de validacion si operacion lo necesita.
- Agregar sesiones persistentes de importacion si se requiere aprobacion diferida o trazabilidad de archivos.
- Agregar generacion masiva segura de tokens si operacion confirma el flujo.
