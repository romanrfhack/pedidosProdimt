# 16 — Validacion SQL Server local

Fecha: 2026-05-28

## Objetivo

Validar que el flujo Fase 1 funciona contra EF Core + SQL Server real, no solo con InMemory, SQLite in-memory o mocks de Playwright.

## Estado correcto de persistencia

- EF Core + SQL Server es el default de ejecucion real.
- `InMemory` se activa solo de forma explicita con `Persistence__Provider=InMemory`.
- SQLite in-memory se usa solo en pruebas automatizadas de persistencia.
- Playwright usa mock API local solo para E2E frontend determinista.

## Levantar SQL Server con Docker Compose

1. Crear configuracion local:

   ```bash
   cp infra/dev/.env.example infra/dev/.env
   ```

2. Editar `infra/dev/.env` y reemplazar `PRODIMT_SQL_SA_PASSWORD` por una contrasena local fuerte.

3. Levantar SQL Server:

   ```bash
   bash scripts/dev/start-sqlserver.sh
   ```

Esto usa:

- Compose: `infra/dev/docker-compose.sqlserver.yml`
- Contenedor: `prodimt-pedidos-sqlserver`
- Volumen: `prodimt-pedidos-sqlserver-data`
- Puerto default: `1433`
- Base esperada: `ProdimtPedidos`

Si `1433` ya esta ocupado, cambiar `PRODIMT_SQL_PORT` en `infra/dev/.env`.

## Alternativa sin Docker

Usar cualquier SQL Server local disponible y exportar la cadena:

```bash
export ConnectionStrings__Pedidos='Server=localhost,1433;Database=ProdimtPedidos;User Id=sa;Password=LOCAL_PASSWORD;TrustServerCertificate=True'
```

Tambien se acepta:

```bash
export PRODIMT_PEDIDOS_CONNECTION_STRING='Server=localhost,1433;Database=ProdimtPedidos;User Id=sa;Password=LOCAL_PASSWORD;TrustServerCertificate=True'
```

No guardar contrasenas reales en el repositorio.

## Aplicar migraciones

```bash
bash scripts/dev/update-database.sh
```

Equivalente manual:

```bash
dotnet tool run dotnet-ef database update --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api
```

El repositorio usa tool manifest local para alinear `dotnet-ef` con EF runtime `10.0.8`:

```bash
dotnet tool restore
dotnet tool run dotnet-ef --version
```

## Iniciar API contra SQL Server

```bash
bash scripts/dev/run-api-sqlserver.sh
```

La API queda en:

```text
http://127.0.0.1:5088
```

En ambiente `Development`, la API aplica migraciones y seed si:

```text
DevelopmentSeed__Enabled=true
DevelopmentSeed__ApplyMigrations=true
```

## Health checks

```bash
curl http://127.0.0.1:5088/health
curl http://127.0.0.1:5088/health/db
```

Respuesta esperada de base:

```json
{
  "status": "ok",
  "database": "reachable"
}
```

Si SQL Server no esta disponible, `/health/db` devuelve error `503` sin exponer connection string ni datos sensibles.

## Smoke test Fase 1

Con la API real corriendo:

```bash
bash scripts/dev/smoke-fase1.sh
```

El smoke valida:

- `/health`.
- `/health/db`.
- Que endpoints admin y cliente rechacen llamadas anonimas con `401` o `403`.
- `POST /api/auth/admin/login`.
- `POST /api/auth/customer-token`.
- Que un JWT de cliente no pueda operar otro `customerId`.
- `GET /api/customer-orders/{demoCustomerId}/today` con JWT de cliente.
- Que la respuesta de cliente no contenga `machine`, `machineId`, `assignedMachineId`, `assignedMachine`, `maquina`, `audit` ni `auditLog`.
- `POST /api/customer-orders/{demoCustomerId}/no-order` con JWT de cliente.
- `POST /api/customer-orders/{demoCustomerId}/submit` con JWT de cliente.
- `currentOrder` despues del envio.
- Que un JWT de cliente no pueda consultar auditoria.
- `GET /api/admin/orders/today` con JWT admin.
- `GET /api/admin/orders/{orderId}` con JWT admin para validar detalle y lineas.
- Segundo pedido del mismo cliente queda con `requiresAdminReview = true`.
- Razon `AdditionalOrderSameDay`.
- `GET /api/admin/orders/pending-review` con JWT admin.
- `GET /api/admin/customers/pending-orders` con JWT admin.
- `POST /api/admin/customers/{customerId}/orders/no-order` con JWT admin.
- `POST /api/admin/customers/{customerId}/orders/submit` con JWT admin.
- Segundo pedido por captura administrativa queda pendiente de revision.
- `POST /api/admin/orders/{orderId}/review` con `AcceptedWithChanges`.
- `GET /api/admin/orders/{orderId}/audit` con JWT admin para validar `OrderSubmitted`, `AdditionalOrderDetected`, `AdminDecisionRecorded`, `AdminManualOrderCaptured`, `AdminNoOrderMarked` y `AdminOrderChanged`.
- CRUD interno de catalogos:
  - crear cliente temporal,
  - actualizar horario de entrega,
  - crear producto temporal,
  - configurar producto frecuente,
  - crear token de cliente,
  - login cliente con token creado,
  - confirmar que cliente ve producto frecuente y no ve maquina,
  - crear/asignar maquina,
  - confirmar que la maquina sigue oculta para cliente,
  - revocar token,
  - confirmar que token revocado no permite login,
  - confirmar que JWT de cliente no accede a catalogos.
- Carga masiva controlada:
  - confirmar que JWT de cliente no accede a importacion,
  - validar y aplicar CSV de clientes,
  - validar y aplicar CSV de productos,
  - validar y aplicar CSV de productos frecuentes,
  - aplicar CSV de maquinas,
  - validar y aplicar CSV de asignaciones cliente-maquina,
  - crear token para cliente importado,
  - confirmar login de cliente importado,
  - confirmar que el cliente importado ve su producto frecuente y no ve maquina.

El smoke modifica datos demo locales. Si se requiere una corrida limpia, reiniciar la base o el volumen local.

## Reset/reseed local

Para borrar la base local `ProdimtPedidos`, reaplicar migraciones y ejecutar el seed de desarrollo:

```bash
bash scripts/dev/reset-database.sh --confirm
```

El script exige `--confirm`, usa configuracion local/dev y rechaza resets ambiguos cuando `ConnectionStrings__Pedidos` ya viene predefinida sin `PRODIMT_ALLOW_DATABASE_RESET=local-dev`.

## Confirmar seed

El seed de desarrollo debe crear:

- Clientes: `Gran Takito`, `Cliente Demo 2`, `Cliente Demo 3`.
- Productos: `#9 1/2`, `#10 1/2`, `#11`, `#15`.
- Maquinas: `Maquina 1`, `Maquina 2`, `Maquina 3`.
- Canales: `Cliente`, `Mostrador`, `Captura administrativa`.
- Productos frecuentes.
- Asignaciones internas de maquina.
- Admin demo `admin`.
- Token demo de cliente para Gran Takito.

El `demoCustomerId` de Angular es:

```text
11111111-1111-1111-1111-111111111111
```

Ese id corresponde a `Gran Takito` en `DevelopmentSeedIds`.

Consulta opcional dentro del contenedor:

```bash
docker exec prodimt-pedidos-sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$PRODIMT_SQL_SA_PASSWORD" -d ProdimtPedidos -Q "SELECT (SELECT COUNT(*) FROM Customers) AS Customers, (SELECT COUNT(*) FROM Products) AS Products, (SELECT COUNT(*) FROM Machines) AS Machines, (SELECT COUNT(*) FROM SalesChannels) AS SalesChannels, (SELECT COUNT(*) FROM CustomerFrequentProducts) AS FrequentProducts, (SELECT COUNT(*) FROM CustomerMachineAssignments) AS MachineAssignments;"
```

Resultado validado en esta sesion:

```text
Customers=3, Products=4, Machines=3, SalesChannels=3, FrequentProducts=4, MachineAssignments=3
```

## Resultado de esta validacion

- Docker disponible.
- SQL Server 2022 levantado con `infra/dev/docker-compose.sqlserver.yml`.
- Primer intento de migracion fallo porque SQL Server aun estaba inicializando el handshake.
- Despues de confirmar que SQL Server estaba listo con `sqlcmd`, la migracion `InitialCreate` se aplico correctamente.
- La API corrio contra SQL Server y aplico seed de desarrollo.
- `/health/db` respondio OK.
- `scripts/dev/smoke-fase1.sh` paso completo.
- En la validacion de autenticacion piloto, `AddPilotAuthentication` se aplico correctamente.
- `reset-database.sh --confirm` reaplico migraciones y seed Development, incluyendo admin demo y token demo.
- `scripts/dev/smoke-fase1.sh` paso completo con JWT cliente/admin, rechazo anonimo, bloqueo de otro `customerId` y auditoria protegida.
- En la validacion de catalogos internos, `AddCatalogManagementSupport` se aplico correctamente.
- `scripts/dev/smoke-fase1.sh` paso completo con alta de cliente/producto/maquina, productos frecuentes, token creado, token revocado y rechazo de catalogos con JWT de cliente.
- En la validacion de carga masiva controlada, `AddCatalogExternalCodesAndImportSupport` debe aplicarse correctamente.
- `scripts/dev/smoke-fase1.sh` valida importacion CSV de clientes/productos/frecuentes/maquinas/asignaciones, auditoria de importacion y rechazo con JWT de cliente.

## Si SQL Server no esta disponible

1. Revisar que Docker este corriendo:

   ```bash
   docker ps
   ```

2. Revisar logs:

   ```bash
   docker logs prodimt-pedidos-sqlserver
   ```

3. Confirmar que el puerto configurado no este ocupado.
4. Confirmar que `PRODIMT_SQL_SA_PASSWORD` cumple politicas de SQL Server.
5. Reintentar `bash scripts/dev/update-database.sh` despues de que el log diga que SQL Server esta listo.

## Pendiente posterior

- Endurecer autenticacion piloto antes de produccion.
- Ampliar UI administrativa de auditoria si se requiere detalle completo.
