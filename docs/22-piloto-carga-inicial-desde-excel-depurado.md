# 22 — Piloto: carga inicial desde Excel depurado

Fecha: 2026-05-28

## Objetivo

Preparar una muestra real depurada del Excel operativo y cargarla a SQL Server local/dev usando CSV controlados.

No se importa directamente el `.xlsm`. No se versionan datos reales. SQL Server sigue siendo la fuente oficial de verdad.

## Que se toma del Excel

Tomar solo datos depurados necesarios para catalogos de Fase 1:

- clientes externos activos para piloto,
- productos/moldes normalizados,
- maquinas internas,
- productos frecuentes por cliente,
- asignaciones internas cliente-maquina.

No tomar:

- macros,
- formulas,
- historico completo,
- datos de pagos o facturacion,
- tokens planos de clientes,
- datos sensibles no necesarios para operar el piloto.

## Mostrador

`Mostrador` es canal interno, no cliente externo.

Si aparece en el Excel, separarlo del archivo de clientes. No debe ir en `customers.csv`.

## Normalizacion de clientes

Checklist:

- Usar un solo nombre comercial por cliente.
- Quitar duplicados obvios por mayusculas, espacios o acentos.
- Mantener telefono solo si es util para operacion.
- Registrar hora deseada en `preferredDeliveryTime` con formato `HH:mm`.
- Registrar ventana en `preferredDeliveryWindowStart` y `preferredDeliveryWindowEnd` con formato `HH:mm`.
- Confirmar que el inicio de ventana no sea mayor al final.
- Usar `deliveryNotes` para instrucciones operativas simples.

## Normalizacion de productos/moldes

Checklist:

- Elegir un nombre unico por molde/producto.
- Unificar variantes como `# 10 1/ 2`, `#10.5` o textos equivalentes.
- No crear dos productos para el mismo molde real.
- Poner descripcion solo si ayuda a operacion interna.

## Maquinas

Checklist:

- Convertir la columna de maquina del Excel al catalogo `machines.csv`.
- Usar numero entero positivo en `number`.
- `name` es opcional y solo interno.
- La maquina asignada nunca debe mostrarse al cliente.

## Productos frecuentes

Usar `customer-frequent-products.csv` para indicar que productos se muestran primero a cada cliente.

Checklist:

- Cada fila debe apuntar a un cliente y un producto existentes o definidos en la misma carpeta de CSV.
- Preferir `customerExternalCode` y `productExternalCode`.
- `defaultQuantity` puede quedar vacio si no hay cantidad sugerida segura.
- `defaultQuantity` no puede ser negativa.
- `sortOrder` controla orden visual; si se repite para el mismo cliente, revisar la advertencia.

## Asignaciones cliente-maquina

Usar `customer-machine-assignments.csv`.

Checklist:

- Cada fila debe apuntar a un cliente y maquina existentes o definidos en la misma carpeta.
- Solo una maquina puede tener `isDefault=true` por cliente.
- Una maquina inactiva no puede ser default.
- Si un cliente tiene asignaciones sin default, el sistema lo permite pero advierte.

## Manejo de x/X

`x` o `X` en el Excel significa **NoOrder** para una fecha de pedido.

No convertir `x/X` en cantidad.
No poner `x/X` en `defaultQuantity`.
No usar `x/X` para catalogos.

En Fase 1, `NoOrder` se registra por el flujo de pedido del dia, no por la carga inicial de catalogos.

## externalCode

`externalCode` es el identificador estable para hacer matching desde datos depurados.

Recomendaciones:

- Usar codigos simples y estables: `C-001`, `P-0105`, `M-01`.
- No usar telefono, nombre real completo ni datos sensibles como codigo.
- No reutilizar codigos para entidades distintas.
- Si no hay `externalCode`, el sistema intenta usar nombre normalizado o numero de maquina y genera advertencia.

## Carpeta local

Colocar muestras reales o depuradas en:

```text
data/local-imports/pilot-sample/
```

Esa carpeta esta ignorada por git. Tambien se ignoran:

- `data/local-imports/`
- `*.real.csv`
- `*.private.csv`

No commitear CSV reales, `.xlsm`, reportes locales, telefonos reales innecesarios, tokens ni secretos.

## Archivos esperados

La carpeta puede tener todos o solo algunos archivos. Los faltantes se reportan como advertencia.

Orden usado por scripts:

1. `products.csv`
2. `machines.csv`
3. `customers.csv`
4. `customer-frequent-products.csv`
5. `customer-machine-assignments.csv`

## Validar

Con la API corriendo:

```bash
bash scripts/dev/validate-import-folder.sh data/local-imports/pilot-sample
```

Variables opcionales:

```bash
export PRODIMT_API_BASE_URL=http://127.0.0.1:5088
export PRODIMT_ADMIN_USERNAME=admin
export PRODIMT_ADMIN_PASSWORD=prodimt-admin-demo
```

Si no se definen usuario y password, los scripts usan defaults de `Development`: `admin` / `prodimt-admin-demo`. No imprimen password ni JWT.

## Reporte de validacion

Se genera en:

```text
data/local-imports/reports/import-validation-YYYYMMDD-HHMMSS.json
data/local-imports/reports/import-validation-YYYYMMDD-HHMMSS.md
```

Leer:

- archivos encontrados,
- archivos faltantes,
- total de filas,
- filas validas,
- errores,
- advertencias,
- creates/updates/deactivates propuestos,
- recomendaciones.

El JSON contiene las respuestas sanitizadas de la API. No guarda JWT ni passwords.

## Corregir errores

Corregir primero errores bloqueantes:

- encabezados faltantes,
- duplicados por `externalCode`,
- duplicados por nombre normalizado,
- cliente/producto/maquina inexistente,
- ventana de entrega invertida,
- cantidades negativas,
- mas de una maquina default.

Luego revisar advertencias:

- telefono vacio o sospechoso,
- falta de `externalCode`,
- actualizaciones existentes,
- producto inactivo como frecuente activo,
- `sortOrder` repetido,
- cliente sin maquina default.

Repetir `validate` hasta que no haya errores bloqueantes.

## Aplicar

Solo aplicar cuando administracion acepte la muestra:

```bash
bash scripts/dev/apply-import-folder.sh data/local-imports/pilot-sample --confirm
```

`apply` valida y aplica cada archivo en orden. Si un archivo tiene errores bloqueantes, no continua con los siguientes.

Reporte:

```text
data/local-imports/reports/import-apply-YYYYMMDD-HHMMSS.json
data/local-imports/reports/import-apply-YYYYMMDD-HHMMSS.md
```

## Verificacion post-importacion

Opciones:

1. Abrir `/admin/catalogos` y revisar clientes, productos, maquinas y configuracion.
2. Abrir `/admin/importacion` para validar un archivo individual si hace falta.
3. Crear tokens de cliente desde el sistema, no por CSV.
4. Ejecutar smoke de carpeta:

   ```bash
   bash scripts/dev/smoke-import-folder.sh
   ```

5. Ejecutar smoke completo:

   ```bash
   bash scripts/dev/smoke-fase1.sh
   ```

## Checklist antes de piloto

- SQL Server local/dev tiene migraciones aplicadas.
- La API corre en `PRODIMT_API_BASE_URL`.
- La carpeta local tiene CSV depurados, no el Excel real.
- `Mostrador` no esta en `customers.csv`.
- `x/X` no aparece como cantidad.
- Todos los `externalCode` son estables y no sensibles.
- `validate` no tiene errores bloqueantes.
- Advertencias revisadas y aceptadas.
- `apply` termino sin errores.
- Cliente no ve maquina asignada.
- Tokens de cliente se generan desde administracion.
- Nada de `data/local-imports/`, `.xlsm`, `*.real.csv` o `*.private.csv` esta trackeado por git.

## Criterios para aceptar la muestra

- Clientes piloto correctos y sin duplicados evidentes.
- Productos/moldes normalizados.
- Maquinas internas correctas.
- Productos frecuentes suficientes para captura rapida.
- Asignaciones default razonables donde apliquen.
- Reporte de validacion sin errores.
- Reporte de aplicacion sin errores.
- Smoke de importacion exitoso.

## Pendientes antes de produccion

- Endurecer autenticacion y manejo de secretos.
- Definir rotacion operativa de tokens de cliente.
- Revisar almacenamiento frontend de JWT.
- Definir si se requiere importacion directa del `.xlsm` en una fase futura.
- Definir cambios administrativos de maquina por pedido cuando entre en alcance.
