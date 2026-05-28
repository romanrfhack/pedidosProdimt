# 14 — Persistencia EF Core y SQL Server

Fecha: 2026-05-27

## Estado

La API usa EF Core con SQL Server como proveedor por defecto. Los repositorios en memoria siguen existiendo solo como fallback configurable para desarrollo cuando no hay SQL Server local.

Configuracion por defecto en `src/Prodimt.Pedidos.Api/appsettings.Development.json`:

```json
{
  "Persistence": {
    "Provider": "SqlServer"
  },
  "DevelopmentSeed": {
    "Enabled": true,
    "ApplyMigrations": true
  }
}
```

Fallback temporal sin SQL Server:

```bash
Persistence__Provider=InMemory dotnet run --project src/Prodimt.Pedidos.Api/Prodimt.Pedidos.Api.csproj --urls http://127.0.0.1:5088
```

La cadena `ConnectionStrings:Pedidos` puede sobrescribirse con la variable de entorno estándar:

```bash
ConnectionStrings__Pedidos='Server=localhost,1433;Database=ProdimtPedidos;User Id=sa;Password=LOCAL_PASSWORD;TrustServerCertificate=True'
```

Tambien se acepta `PRODIMT_PEDIDOS_CONNECTION_STRING` para scripts o entornos donde sea mas practico manejar una sola variable.

## Modelo configurado

Entidades configuradas con Fluent API:

- `Customer`
- `Product`
- `CustomerFrequentProduct`
- `Machine`
- `CustomerMachineAssignment`
- `SalesChannel`
- `Order`
- `OrderLine`

Relaciones configuradas:

- `Customer` 1:N `Order`.
- `Order` 1:N `OrderLine`.
- `Product` 1:N `OrderLine`.
- `Customer` N:N `Product` mediante `CustomerFrequentProduct`.
- `Customer` N:N `Machine` mediante `CustomerMachineAssignment`.
- `SalesChannel` 1:N `Order`.
- `Machine` 1:N `OrderLine` como `AssignedMachineId` opcional.

## Datos semilla de desarrollo

El seed vive en `Prodimt.Pedidos.Infrastructure/Persistence/Seed`.

Datos creados:

- Clientes: `Gran Takito`, `Cliente Demo 2`, `Cliente Demo 3`.
- Productos: `#9 1/2`, `#10 1/2`, `#11`, `#15`.
- Maquinas: `Maquina 1`, `Maquina 2`, `Maquina 3`.
- Canales: `Cliente`, `Mostrador`, `Captura administrativa`.
- Productos frecuentes y asignaciones internas de maquina.

Estos datos son solo para desarrollo y no representan datos reales sensibles.

## Migraciones

La migracion inicial fue creada con:

```bash
dotnet ef migrations add InitialCreate --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api --output-dir Persistence/Migrations
```

Aplicar a SQL Server local:

```bash
dotnet ef database update --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api
```

Tambien se puede usar el script de desarrollo:

```bash
bash scripts/dev/update-database.sh
```

## Validacion local SQL Server

Ver `docs/16-validacion-sql-server-local.md` para levantar SQL Server con Docker Compose, iniciar la API contra SQL Server y correr el smoke test de Fase 1.

Resultado de validacion local 2026-05-27:

- SQL Server 2022 local en Docker: levantado.
- Migracion `InitialCreate`: aplicada.
- Seed de desarrollo: aplicado.
- `/health/db`: `{"status":"ok","database":"reachable"}`.
- Smoke Fase 1 contra API real + SQL Server: exitoso.

Nota: durante esta sesion `dotnet-ef` estaba en version `10.0.7` y el runtime EF en `10.0.8`; la migracion se creo correctamente, pero conviene actualizar la herramienta para evitar diferencias futuras.

## Pruebas

Las pruebas de persistencia usan SQLite in-memory para validar reglas y repositorios sin depender de SQL Server local.

Esto no cambia la decision arquitectonica: SQL Server sigue siendo la fuente de verdad para ejecucion real.
