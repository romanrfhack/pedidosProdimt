# I01 — Fundamentos del piloto

## Problema y alcance

El modelo inicial no distingue unidades, aplica la máquina del cliente a todos sus productos y no representa llave ni cobro diario. Antes de cargar una ruta necesitamos datos explícitos y un resumen de demanda que no haga pasar un pedido por producción o entrega concluida.

El incremento añade unidades, configuración interna del piloto, preferencia de máquina por producto, validaciones de captura, snapshots de contexto y consulta administrativa de demanda/preparación. El paquete de candidatos privados es preparación para validar; no es una importación de pedidos actuales.

## Aceptación funcional

- [x] Una unidad desconocida se distingue de kg, pieza, docena y paquete.
- [x] Se rechaza captura con producto inexistente/inactivo o unidad no definida.
- [x] Llave y condición de pago admiten desconocido; las instrucciones interiores son internas.
- [x] Orden visual y posición de interpretación son campos distintos; aprobación explícita y versión del perfil.
- [x] Un pedido con dos productos puede tomar dos máquinas preferidas distintas.
- [x] Contexto de producto/unidad y entrega preservado al crear pedido; no reinterpretar histórico al cambiar catálogo.
- [x] Fecha del negocio independiente de la zona del servidor.
- [x] Revisión administrativa sólo sobre estado pendiente.
- [x] Consulta protegida agrupa demanda por máquina/producto/unidad, con pendientes separados.
- [x] Vista imprimible por cliente/ruta indica “por producir / por preparar”.
- [x] Ningún endpoint de cliente expone llaves, instrucciones interiores o máquinas.
- [x] CSV antiguos no borran nuevos datos internos por omitir columnas opcionales.
- [x] Registro de continuidad y pruebas persistido en git, sin datos reales.

## Fuera de este incremento

Importación diaria de pedidos, parser de WhatsApp, revisión de correcciones con reemplazo de líneas, identidad/idempotencia de cada operación, división de una línea entre máquinas, registro físico de producción/entrega, precios, saldos, tickets monetarios y pagos. Estos elementos conservan su lugar en el plan general.

La agrupación inicial usa `RouteCode` y `RouteStopOrder` del cliente, con snapshot en pedido. Es configuración básica del piloto; no representa viajes, asignación de repartidores ni un catálogo avanzado de rutas.

## Verificación

Resultados locales: 113 pruebas .NET correctas (5 + 108), build Release sin advertencias; 23 E2E con API mock correctas; build Angular correcto y comprobación visual móvil/impresión. Reemplazar posiciones de perfil usa transacción: si la inserción falla, versión, auditoría y conjunto previo se revierten.

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NG_CLI_ANALYTICS=false
dotnet restore src/Prodimt.Pedidos.sln --disable-parallel -m:1 -nr:false
dotnet build src/Prodimt.Pedidos.sln -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test src/Prodimt.Pedidos.sln -c Release --no-build --no-restore -m:1 -nr:false
# Desde apps/prodimt-pedidos-web:
npm ci
npm run build
# Desde tests/e2e:
npm ci
npx playwright install --with-deps chromium
npm test -- --workers=1
```

En este entorno se usó un Chromium preinstalado mediante configuración externa porque falló la descarga del navegador del lockfile. No cambiar el lockfile para imitar esa adaptación del entorno. CI instala el navegador correspondiente.

Una prueba con API mock no equivale a prueba de persistencia SQL Server. Generar una migración tampoco demuestra que se haya aplicado en una instancia real.

## Contratos resumidos

- `Product.unitCode`: `null`, `kg`, `piece`, `dozen`, `package`.
- Cliente administrativo: `routeCode`, `routeStopOrder`, `hasStoreKey` nullable, `paymentTermCode` (`cash_on_delivery`, `credit`, desconocido), `internalDeliveryInstructions`, `orderingProfileVersion`, `isOrderProfileApproved`.
- Frecuente: `preferredMachineId`, `interpretationPosition`; al guardar se envía `approveForInterpretation` explícito. Cambios posteriores invalidan aprobación cuando afectan significado.
- En CSV, columna nueva ausente preserva el valor existente; presente vacía permite limpiarlo. Importar frecuentes incrementa versión y revoca aprobación.
- `GET /api/admin/operations/plan?date=YYYY-MM-DD&routeCode=...`, con `AdminAccess`.
- Demanda sólo `Submitted`/`Accepted`, agrupada por máquina, producto, nombre histórico y unidad. Las unidades desconocidas se muestran con advertencia y se excluyen de totales.
- Nuevos pedidos guardan snapshots; las consultas históricas no deducen unidades desde el catálogo actual.
- Revisión: `expectedVersion` y conflicto HTTP 409 si el pedido cambió o dejó de estar pendiente; índice único cliente/fecha/secuencia protege colisiones de creación. No deduplica reenvíos secuenciales.

Migración: `20261024164610_AddPilotCatalogAndOrderSnapshots`. Agrega columnas nullable para desconocidos y restricciones de unicidad; no convierte datos previos a kg.


## Comprobación adicional de HTTP y dependencias

Los ejemplos versionados de productos ya incluyen unidades explícitas. `scripts/dev/README-smokes.md` describe los dos smokes autenticados ejecutados con la API real y almacenamiento InMemory: ambos correctos. No reemplazan su ejecución pendiente contra SQL Server.

El primer CI permitió corregir la sincronización visual de la aprobación del perfil. La prueba exige que una edición posterior desmarque la casilla y envíe aprobación false al servidor. Tras elevar únicamente parches compatibles de Angular 21 y dependencias de herramientas, la suite completa volvió a pasar (23/23). La auditoría npm completa no reporta vulnerabilidades en el catálogo consultado en esta sesión.
