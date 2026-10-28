# AGENTS.md — PRODIMT Pedidos

## Alcance vigente desde 2026-10-24

El usuario autorizó el plan integral, I01, I02, la instalación/ensayo I03 y el ajuste operativo I04. Leer primero `CONTINUAR.md` y `docs/proyecto/{ESTADO,PLAN,DECISIONES,INCREMENTO-01,INCREMENTO-02,INCREMENTO-03,INCREMENTO-04,INCREMENTO-05,INCREMENTO-06,INCREMENTO-07}.md`; estos documentos prevalecen sobre el alcance histórico de mayo que sigue abajo. Cada sesión debe actualizar estado y bitácora y comprobar que el trabajo se guardó en el remoto.

I01 permite unidades explícitas, perfiles aprobables/versionados, preferencia de máquina por cliente-producto, datos internos de entrega/cobro y consulta de demanda por máquina/ruta. No registra todavía producción física, entregas, cobros ni WhatsApp real. Los datos reales del piloto y las instrucciones de acceso a locales nunca se publican en este repositorio público. La ausencia de iconos es desconocida, no false. Una X en un renglón de producto no cancela todo el pedido del cliente.

I02 permite fecha de servicio administrativa, CSV de pedidos diarios con vista previa, claves persistentes para reintentos y correcciones explícitas auditadas. Un pedido adicional y una corrección son acciones diferentes. No interpretar automáticamente mensajes de WhatsApp ni inferir pedidos de cantidades históricas. Validación técnica en SQL Server con datos ficticios no equivale a despliegue o carga de la ruta real.

I03-A prepara una instalación aislada Linux/WSL con Docker, API y frontend en un mismo origen, SQL Server Express y administrador propio. No ejecuta automáticamente cambios en el VPS ni completa la semana de ensayo. Nunca empaquetar secretos locales ni datos reales dentro de la imagen Docker o el repositorio; la hoja privada de pendientes conserva los valores confirmados sin inventar unidades.

I04 añade avisos persistentes y reconocimiento de cambios de demanda, configuración enfocada por cliente y protección de edición concurrente del perfil. Reconocer un aviso no registra producción física. El usuario eligió el VPS y dominio del piloto; preparar instalación por etapas con inventario previo, sin sobrescribir otros proyectos. La captura desde WhatsApp se mantiene asistida por administración hasta implementar y validar su interpretación; un segundo mensaje no se suma automáticamente como otro pedido autorizado.

I06, autorizado el 28 de octubre tras confirmar la carga de clientes, rediseña el catálogo con búsqueda principal, modal de alta/edición, tarjetas según el estado diario y descansos semanales configurables. Ver docs/proyecto/INCREMENTO-06.md. Descansar no desactiva al cliente ni crea NoOrder; los pedidos vigentes prevalecen y la lectura no modifica demanda. Preservar el CSV existente y preparar migración aditiva con respaldo para el despliegue nativo.

I07, autorizado el 28 de octubre, permite captura administrativa abreviada determinista según posiciones explícitas del perfil del cliente y abreviaturas configuradas por producto. Ver `docs/proyecto/INCREMENTO-07.md`. Mostrar desglose antes de guardar; aprobación explícita para interpretación, unidades confirmadas y alternativa manual. No inferir el significado de HN ni convertir mensajes completos de WhatsApp automáticamente. Guardado y reintento durables, control de perfil vigente y pedido existente.

## Que es

I05, autorizado el 26 de octubre, añade cambio de contraseña propia para administración después de recuperar acceso. Ver `docs/proyecto/INCREMENTO-05.md`. No permite elegir otra cuenta por Id/nombre ni restablecimiento público. Contraseña/auditoría atómicas, protección de concurrencia e invalidación de sesiones administrativas anteriores; no publicar datos de acceso compartidos en el chat.

PRODIMT Pedidos es la aplicacion web mobile first para capturar pedidos de clientes de PRODIMT y reemplazar gradualmente el flujo manual basado en WhatsApp, llamadas, mensajes en grupo y captura en Excel.

SQL Server sera la fuente oficial de verdad. El Excel actual es solo referencia inicial para entender clientes, productos, maquinas y operacion.

## Fase 1

Incluye:

- Captura de pedido del dia desde celular.
- Productos frecuentes y cantidades sugeridas.
- Accion explicita "No pedir hoy".
- Deteccion de pedido tardio despues de las 10:00 a.m.
- Deteccion de segundo pedido del mismo cliente en el mismo dia.
- Revision administrativa basica para pedidos tardios o adicionales.
- Auditoria persistente minima de eventos principales del pedido.
- Autenticacion piloto con JWT Bearer para cliente por token y administracion basica.
- Vista administrativa inicial de pedidos del dia y pendientes de revision.
- Administracion operativa basica: detalle de lineas, clientes pendientes, captura en nombre de cliente, `NoOrder` administrativo y `AcceptedWithChanges` real.
- CRUD interno minimo de catalogos: clientes, productos/moldes, productos frecuentes, maquinas, asignacion cliente-maquina, tokens de cliente y usuarios admin basicos.
- Carga masiva controlada por CSV con validacion `dry-run`, aplicacion confirmada y auditoria.
- Modelo inicial para maquina asignada como dato interno.

No implementar todavia:

- WhatsApp real, Twilio o Meta WhatsApp Business API.
- Autenticacion definitiva, roles completos, recuperacion de contrasena, 2FA o administracion avanzada de usuarios.
- Produccion por maquina completa.
- Embarques, repartidores, rutas avanzadas.
- Estadisticas avanzadas, proyecciones, IA.
- Importacion completa del Excel.
- Importacion directa ciega del `.xlsm` operativo.
- Envio de mensajes, jobs automaticos o background workers.
- Facturacion o pagos.

## Stack

- Backend: .NET 10, C#, ASP.NET Core Web API, Clean Architecture.
- Persistencia objetivo: SQL Server con Entity Framework Core.
- Frontend: Angular 21, mobile first, CSS estandar.
- Pruebas: xUnit para backend y Playwright para E2E.

## Reglas criticas

- `x` o `X` en Excel significa que el cliente no pidio; debe mapearse a estado `NoOrder`.
- La hora limite normal es 10:00 a.m.
- Pedido despues de las 10:00 a.m. se captura, se marca tardio y queda pendiente de revision administrativa.
- Segundo pedido del mismo cliente en el mismo dia queda pendiente de revision administrativa.
- Administracion puede aceptar, rechazar o aceptar con cambios un pedido pendiente.
- Endpoints administrativos requieren `AdminAccess`.
- Endpoints de catalogos internos requieren `AdminAccess`; un JWT de cliente no puede usarlos.
- Endpoints de cliente requieren `CustomerAccess` y solo permiten operar el `CustomerId` del JWT.
- Algunos clientes tienen hora o ventana deseada de entrega opcional.
- `Mostrador` es canal interno, no cliente externo.
- La maquina asignada es informacion interna.
- El cliente nunca debe ver la maquina asignada.
- Los tokens de cliente se guardan hasheados; el token plano solo se muestra al crearlo.
- No importar tokens planos de cliente por CSV; generar tokens desde el sistema despues de importar clientes.

## Convenciones

- `Domain` no debe depender de EF Core, ASP.NET ni infraestructura.
- `Application` define casos de uso, DTOs e interfaces; no accede directo a SQL Server.
- `Infrastructure` contiene EF Core, DbContext y repositorios.
- `Api` contiene endpoints, OpenAPI y configuracion de DI.
- No poner logica de negocio en controllers/endpoints ni componentes Angular.
- No agregar secretos, credenciales reales o tokens.
- No commitear el Excel operativo real, `.xlsm`, CSV reales, reportes locales ni datos sensibles.
- Los CSV reales o muestras depuradas privadas deben colocarse en `data/local-imports/`, que esta ignorado por git.
- Mantener el alcance dentro de Fase 1.

## Comandos

Backend:

```bash
dotnet restore src/Prodimt.Pedidos.sln
dotnet build src/Prodimt.Pedidos.sln
dotnet run --project src/Prodimt.Pedidos.Api/Prodimt.Pedidos.Api.csproj --urls http://127.0.0.1:5088
```

Aplicar migraciones EF Core:

```bash
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api
```

SQL Server local con Docker:

```bash
cp infra/dev/.env.example infra/dev/.env
bash scripts/dev/start-sqlserver.sh
bash scripts/dev/update-database.sh
bash scripts/dev/run-api-sqlserver.sh
bash scripts/dev/smoke-fase1.sh
bash scripts/dev/reset-database.sh --confirm
bash scripts/dev/validate-import-folder.sh data/local-imports/pilot-sample
bash scripts/dev/apply-import-folder.sh data/local-imports/pilot-sample --confirm
bash scripts/dev/smoke-import-folder.sh
```

Fallback temporal sin SQL Server:

```bash
Persistence__Provider=InMemory dotnet run --project src/Prodimt.Pedidos.Api/Prodimt.Pedidos.Api.csproj --urls http://127.0.0.1:5088
```

Frontend:

```bash
cd apps/prodimt-pedidos-web
npm install
npm run start -- --host 127.0.0.1 --port 4200
```

Pruebas:

```bash
dotnet test src/Prodimt.Pedidos.sln
cd apps/prodimt-pedidos-web
npm run build
cd ../../tests/e2e
npm install
npm test
```

## Documentos a leer antes de modificar

1. `README.md`
2. `docs/08-contexto-para-codex.md`
3. `docs/10-decisiones-operativas-confirmadas.md`
4. `docs/11-reglas-de-negocio-fase-1.md`
5. `docs/12-flujos-fase-1.md`
6. `docs/02-alcance-y-etapas.md`
7. `docs/03-requerimientos-funcionales.md`
8. `docs/06-modelo-datos-inicial.md`
9. `docs/07-backlog-mvp.md`
10. `docs/21-carga-masiva-controlada-fase-1.md`
11. `docs/22-piloto-carga-inicial-desde-excel-depurado.md`

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, invoke the `skill` tool with `skill: "graphify"` before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
