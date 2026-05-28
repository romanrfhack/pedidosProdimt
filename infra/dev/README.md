# Local SQL Server

This folder contains the local SQL Server setup for PRODIMT Pedidos development.

## Setup

1. Copy the example environment file:

   ```bash
   cp infra/dev/.env.example infra/dev/.env
   ```

2. Edit `infra/dev/.env` and replace `PRODIMT_SQL_SA_PASSWORD` with a local strong password.

3. Start SQL Server:

   ```bash
   bash scripts/dev/start-sqlserver.sh
   ```

The container uses:

- Container: `prodimt-pedidos-sqlserver`
- Volume: `prodimt-pedidos-sqlserver-data`
- Database expected by the app: `ProdimtPedidos`
- Default host port: `1433`

If `1433` is already in use, change `PRODIMT_SQL_PORT` in `infra/dev/.env`.

## Apply Migrations

```bash
bash scripts/dev/update-database.sh
```

The repo uses a local `dotnet-ef` tool manifest. Run `dotnet tool restore` after cloning.

## Run API Against SQL Server

```bash
bash scripts/dev/run-api-sqlserver.sh
```

The API starts in `Development`, applies migrations and seed on startup when `DevelopmentSeed:Enabled` and `DevelopmentSeed:ApplyMigrations` are true.

## Smoke Test

With the API running:

```bash
bash scripts/dev/smoke-fase1.sh
```

The smoke test uses demo data and creates local development orders. Reset the local database/container if you need a clean run.

## Reset And Reseed

This deletes the local development database, reapplies migrations, and starts the API once to run the development seed:

```bash
bash scripts/dev/reset-database.sh --confirm
```

Do not use it with production or shared databases.
