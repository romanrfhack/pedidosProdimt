# AGENTS.md — PRODIMT Pedidos

## Que es

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
- Vista administrativa inicial de pedidos del dia y pendientes de revision.
- Modelo inicial para maquina asignada como dato interno.

No implementar todavia:

- WhatsApp real, Twilio o Meta WhatsApp Business API.
- Login real con JWT y roles completos.
- Produccion por maquina completa.
- Embarques, repartidores, rutas avanzadas.
- Estadisticas avanzadas, proyecciones, IA.
- Importacion completa del Excel.
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
- Algunos clientes tienen hora o ventana deseada de entrega opcional.
- `Mostrador` es canal interno, no cliente externo.
- La maquina asignada es informacion interna.
- El cliente nunca debe ver la maquina asignada.

## Convenciones

- `Domain` no debe depender de EF Core, ASP.NET ni infraestructura.
- `Application` define casos de uso, DTOs e interfaces; no accede directo a SQL Server.
- `Infrastructure` contiene EF Core, DbContext y repositorios.
- `Api` contiene endpoints, OpenAPI y configuracion de DI.
- No poner logica de negocio en controllers/endpoints ni componentes Angular.
- No agregar secretos, credenciales reales o tokens.
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
dotnet ef database update --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api
```

SQL Server local con Docker:

```bash
cp infra/dev/.env.example infra/dev/.env
bash scripts/dev/start-sqlserver.sh
bash scripts/dev/update-database.sh
bash scripts/dev/run-api-sqlserver.sh
bash scripts/dev/smoke-fase1.sh
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
