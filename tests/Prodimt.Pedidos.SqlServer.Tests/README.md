# Pruebas HTTP y SQL Server del piloto

Este proyecto usa el proveedor SQL Server real y aplica las migraciones del repositorio. Está fuera de la solución principal para que una ejecución local sin SQL Server no se confunda con una validación relacional exitosa. El job `sqlserver` de `.github/workflows/ci.yml` lo ejecuta expresamente.

## Ejecución local

Configurar `PRODIMT_SQL_TEST_CONNECTION` mediante el mecanismo local de secretos, apuntando a una instancia **aislada de pruebas**. El usuario SQL necesita crear y eliminar bases. La conexión no debe apuntar a una instancia operativa.

```bash
bash scripts/dev/smoke-i02-sqlserver.sh
```

La suite genera una base `ProdimtI02Test_<guid>`, ignora el catálogo de la cadena al crearla y elimina únicamente esa base al terminar. Los datos son ficticios y se generan en cada caso. Nunca elimina ni migra una base cuyo nombre venga de configuración. Si falta la conexión, el proyecto falla; no sustituye SQL Server por SQLite ni marca las pruebas como omitidas.

## Cobertura

- Migración desde base limpia, segunda aplicación y consistencia del modelo.
- Vista previa sin escritura, persistencia entre instancias de la API y deduplicación entre lotes.
- Reintentos concurrentes con la misma clave y conflicto ante un contenido distinto.
- Rollback del lote completo ante una restricción SQL que falla después de una vista previa válida; reintento de la misma operación después de retirar la restricción.
- Corrección que reemplaza cantidades, conserva historial y rechaza versiones obsoletas o cambios de contenido con la misma clave.
- Dos correcciones simultáneas: una gana, la otra devuelve conflicto y no deja un recibo de éxito.
- Pedidos de fecha pasada consultables por fecha, revisión administrativa y demanda autorizada sólo después de aceptar.
- Intercambio de posiciones de perfil y conservación de filas/versión ante un fallo de clave foránea.

El reloj del negocio es fijo para reproducir fechas y corte. Los tokens JWT se firman con el reloj real y conservan su validación normal. En CI, SQL Server usa un contenedor efímero con contraseña aleatoria enmascarada; no se cargan clientes reales ni se publican artefactos con datos operativos.
