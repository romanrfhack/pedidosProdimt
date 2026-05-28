# Plantillas CSV de importacion

Estas plantillas son para carga masiva controlada de Fase 1. No importar directamente el `.xlsm` operativo.

Los ejemplos en `examples/` son demo genericos. No agregar datos reales, telefonos reales sensibles ni tokens.

## Archivos

- `customers.csv`: clientes externos.
- `products.csv`: productos o moldes.
- `machines.csv`: maquinas internas.
- `customer-frequent-products.csv`: productos frecuentes por cliente.
- `customer-machine-assignments.csv`: asignacion interna cliente-maquina.

Orden recomendado para carpeta:

1. `products.csv`
2. `machines.csv`
3. `customers.csv`
4. `customer-frequent-products.csv`
5. `customer-machine-assignments.csv`

## customers.csv

```csv
externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes
```

- `externalCode`: opcional, recomendado. Codigo estable no sensible.
- `name`: requerido. Nombre comercial normalizado.
- `phoneNumber`: opcional. Caracteres sospechosos generan advertencia.
- `isActive`: opcional. Default `true`.
- `preferredDeliveryTime`: opcional en `HH:mm`.
- `preferredDeliveryWindowStart`: opcional en `HH:mm`.
- `preferredDeliveryWindowEnd`: opcional en `HH:mm`.
- `deliveryNotes`: opcional.

Validaciones principales: nombre requerido, `externalCode` duplicado, nombre normalizado duplicado, horas validas y ventana inicial no mayor a final.

## products.csv

```csv
externalCode,name,description,isActive
```

- `externalCode`: opcional, recomendado.
- `name`: requerido. Nombre normalizado del molde/producto.
- `description`: opcional.
- `isActive`: opcional. Default `true`.

Validaciones principales: nombre requerido, `externalCode` duplicado y nombre normalizado duplicado.

## machines.csv

```csv
externalCode,number,name,isActive
```

- `externalCode`: opcional, recomendado.
- `number`: requerido. Entero positivo.
- `name`: opcional.
- `isActive`: opcional. Default `true`.

Validaciones principales: numero requerido positivo, `externalCode` duplicado y numero duplicado.

## customer-frequent-products.csv

```csv
customerExternalCode,customerName,productExternalCode,productName,defaultQuantity,sortOrder,isActive
```

- `customerExternalCode`: recomendado.
- `customerName`: fallback si no hay codigo externo.
- `productExternalCode`: recomendado.
- `productName`: fallback si no hay codigo externo.
- `defaultQuantity`: opcional. No puede ser negativa. No usar `x/X`.
- `sortOrder`: opcional. Si viene vacio o <= 0, se normaliza por posicion.
- `isActive`: opcional. Default `true`.

Validaciones principales: cliente existente, producto existente, cantidad no negativa, producto frecuente duplicado por cliente y advertencia si `sortOrder` se repite.

## customer-machine-assignments.csv

```csv
customerExternalCode,customerName,machineExternalCode,machineNumber,isDefault,notes
```

- `customerExternalCode`: recomendado.
- `customerName`: fallback si no hay codigo externo.
- `machineExternalCode`: recomendado.
- `machineNumber`: fallback si no hay codigo externo.
- `isDefault`: opcional. Default `false`.
- `notes`: opcional, interna.

Validaciones principales: cliente existente, maquina existente, solo una default por cliente y maquina inactiva no puede ser default.

## Booleanos y horas

Booleanos aceptados:

- `true` / `false`
- `1` / `0`
- `si` / `no`
- `sí` / `no`

Horas:

- usar `HH:mm`, por ejemplo `09:30`, `10:00`, `14:15`.

## Datos reales

Colocar muestras privadas en:

```text
data/local-imports/pilot-sample/
```

Esa ruta esta ignorada por git. No versionar reportes locales ni archivos con sufijos `*.real.csv` o `*.private.csv`.
