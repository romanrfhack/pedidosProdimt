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
externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes,routeCode,routeStopOrder,hasStoreKey,paymentTermCode,internalDeliveryInstructions
```

- `externalCode`: opcional, recomendado. Codigo estable no sensible.
- `name`: requerido. Nombre comercial normalizado.
- `phoneNumber`: opcional. Caracteres sospechosos generan advertencia.
- `isActive`: opcional. Default `true`.
- `preferredDeliveryTime`: opcional en `HH:mm`.
- `preferredDeliveryWindowStart`: opcional en `HH:mm`.
- `preferredDeliveryWindowEnd`: opcional en `HH:mm`.
- `deliveryNotes`: opcional; puede formar parte del flujo de cliente. No colocar aqui instrucciones de acceso privadas.
- `routeCode`: codigo interno de ruta, maximo 80 caracteres.
- `routeStopOrder`: entero positivo y requiere `routeCode`; identifica el orden de visita, no al cliente.
- `hasStoreKey`: `true`/`false` o vacio si no se ha confirmado. La ausencia de icono en una fuente no equivale a `false`.
- `paymentTermCode`: `cash_on_delivery` o `credit`; vacio si no se ha confirmado. No se presume credito semanal.
- `internalDeliveryInstructions`: instrucciones privadas sobre acceso y lugar de entrega, maximo 1000 caracteres. Solo para administracion.

Los cinco encabezados nuevos son opcionales. Si se omite una columna, se conserva el dato existente. Si la columna esta presente con celda vacia, se guarda `null` (desconocido o sin dato). Para borrar una ruta con parada, vaciar ambas columnas. Un cliente nuevo sin estos datos queda pendiente de confirmacion.

Validaciones principales: nombre requerido, `externalCode` duplicado, nombre normalizado duplicado, horas validas y ventana inicial no mayor a final.

## products.csv

```csv
externalCode,name,description,isActive,unitCode
```

- `externalCode`: opcional, recomendado.
- `name`: requerido. Nombre normalizado del molde/producto.
- `description`: opcional.
- `isActive`: opcional. Default `true`.
- `unitCode`: `kg`, `piece`, `dozen` o `package`. El encabezado es opcional: si se omite conserva la unidad existente; si esta presente y vacio elimina la unidad a `null`. Un producto nuevo sin unidad queda por confirmar; no se convierte automaticamente en kilogramos.

Las unidades son de venta. Este incremento no convierte paquetes/docenas a kilogramos ni define el contenido de cada paquete.

Cambiar el nombre, la unidad o el estado activo de un producto existente deja pendientes de revision todos los perfiles de cliente que lo usan, incrementa su version y registra la invalidacion. Una actualizacion que solo modifica la descripcion no revoca su aprobacion.

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
customerExternalCode,customerName,productExternalCode,productName,defaultQuantity,sortOrder,isActive,preferredMachineExternalCode,preferredMachineNumber,interpretationPosition
```

- `customerExternalCode`: recomendado.
- `customerName`: fallback si no hay codigo externo.
- `productExternalCode`: recomendado.
- `productName`: fallback si no hay codigo externo.
- `defaultQuantity`: opcional. No puede ser negativa. No usar `x/X`.
- `sortOrder`: opcional. Si viene vacio o <= 0, se normaliza por posicion.
- `isActive`: opcional. Default `true`.
- `preferredMachineExternalCode`: codigo de la maquina activa preferida para este cliente y producto.
- `preferredMachineNumber`: numero de la maquina activa si no se usa codigo. Si se envian codigo y numero, deben identificar la misma maquina.
- `interpretationPosition`: entero positivo unico para el cliente. Es independiente de `sortOrder`; no se infiere del orden visual ni del tamano del producto.

Los tres encabezados nuevos son opcionales. Omitir ambos encabezados de maquina conserva la preferencia de una fila existente; incluir cualquiera de ellos vacio sin otra referencia elimina la preferencia. Omitir `interpretationPosition` conserva el valor previo de la fila; incluirlo vacio lo elimina. Las filas nuevas sin estos datos quedan con valores desconocidos.

Cada aplicacion de productos frecuentes conserva el comportamiento de reemplazar la lista de los clientes presentes, incrementa la version de su perfil y revoca su aprobacion para interpretar mensajes abreviados. La importacion nunca aprueba el perfil: administracion debe revisarlo y aprobarlo explicitamente desde catalogos. Cambiar `sortOrder` no crea una posicion de interpretacion.

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
