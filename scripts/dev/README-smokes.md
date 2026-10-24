# Comprobaciones funcionales de API

Los scripts usan HTTP y autenticación reales, con datos sintéticos. No son las pruebas de interfaz con API simulada de `tests/e2e`.

## SQL Server (modo predeterminado)

Con la API local y una base de desarrollo recién preparada:

```bash
PRODIMT_API_BASE_URL=http://127.0.0.1:5088 bash scripts/dev/smoke-fase1.sh
PRODIMT_API_BASE_URL=http://127.0.0.1:5088 bash scripts/dev/smoke-import-folder.sh
```

`smoke-fase1` exige que `/health/db` confirme conectividad. No ejecutar estos scripts en una base operativa: crean clientes, productos, máquinas, tokens y pedidos sintéticos. La secuencia de Fase 1 espera que los clientes de demostración no tengan pedidos previos para el día.

## API real con almacenamiento InMemory

Cuando SQL Server no esté disponible, puede verificarse el flujo HTTP con el proveedor de desarrollo:

```bash
ASPNETCORE_ENVIRONMENT=Development Persistence__Provider=InMemory \
  dotnet run --project src/Prodimt.Pedidos.Api --urls http://127.0.0.1:5099
```

Con esa API recién iniciada, desde otra terminal:

```bash
PRODIMT_API_BASE_URL=http://127.0.0.1:5099 PRODIMT_SMOKE_PROVIDER=InMemory \
  bash scripts/dev/smoke-fase1.sh
PRODIMT_API_BASE_URL=http://127.0.0.1:5099 \
  bash scripts/dev/smoke-import-folder.sh
```

El modo `InMemory` debe seleccionarse explícitamente. Espera que `/health/db` devuelva 503 porque la persistencia relacional no está registrada y lo informa en la salida. **No valida SQL Server, migraciones, persistencia tras reinicio ni concurrencia de base real.** Reiniciar la API limpia los datos InMemory antes de repetir la secuencia completa.

En entornos que aíslan la red de cada comando, iniciar API y ejecutar los scripts dentro del mismo comando/proceso de orquestación; al terminar detener esa API.

## Cobertura del incremento I01

- Productos demo con unidades explícitas (`kg` y `dozen`), tanto por CSV como por alta administrativa.
- Captura de pedidos desde catálogos importados y conservación de la unidad por línea.
- Ausencia de máquinas, llaves, ruta, condiciones de cobro e instrucciones internas en respuestas de cliente.
- Plan operativo protegido: 401 sin sesión y 403 con sesión de cliente.
- Demanda aceptada separada de revisión pendiente y de `NoOrder`.
- Cantidades por producto y unidad, sin sumar docenas con kilogramos.
- Revisión administrativa con la versión del detalle consultado.

Estos scripts no validan producción terminada, entrega física ni cobro. La salida del plan representa «Por producir» y «Por preparar».
