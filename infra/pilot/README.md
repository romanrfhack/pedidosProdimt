# Piloto aislado PRODIMT · I03

Este kit prepara Linux x86-64 o WSL2 con Docker. No instala Docker, no modifica servicios del host y no carga datos reales. La API ASP.NET Core y Angular se sirven desde un mismo contenedor en `http://127.0.0.1:5098`; SQL Server 2022 Express usa su propio volumen y no publica el puerto 1433.

Para el VPS y `pedidos.prodimt.site`, siga [DEPLOY-VPS.md](DEPLOY-VPS.md). El primer paso es un inventario de sólo lectura. Los pasos posteriores preparan únicamente el virtual host del piloto y HTTPS; no modifican las otras aplicaciones del servidor.

## Requisitos y primera instalación

- Docker Engine o Docker Desktop con Compose v2 y soporte `up --wait`; usuario con acceso al daemon.
- Python 3, curl, al menos 5 GiB de RAM disponibles para Docker y 10 GiB libres para compilación/datos. Docker Desktop debe tener también ese espacio en su disco virtual.
- Acceso de salida a los registros de contenedores y a npm/NuGet durante la compilación. Node 24 y .NET 10 se ejecutan dentro de las etapas Docker; no se instalan en el host.

Desde la carpeta `app/` extraída del kit (o la raíz del repositorio):

```bash
bash scripts/pilot/initialize.sh
bash scripts/pilot/preflight.sh
bash scripts/pilot/start.sh
```

`initialize.sh` genera una sola vez `infra/pilot/.env` y `infra/pilot/ACCESO-PRIVADO.txt`, ambos con permisos 600. El segundo archivo contiene el acceso administrativo inicial. Guarde ambos en un lugar privado; no comparta su contenido ni la salida de `docker compose config`/`docker inspect`, que puede incluir secretos. Git y el contexto Docker excluyen los archivos privados y backups. Si existe configuración se conserva; si existe el volumen SQL y falta `.env`, la inicialización se detiene para recuperarla sin cambiar contraseñas.

`preflight.sh` valida permisos, Docker/Compose, arquitectura, RAM, espacio, configuración y disponibilidad del puerto. No detiene otro servicio que ocupe el puerto. `start.sh` compila la imagen Production, espera SQL, crea el login limitado, ejecuta la inicialización explícita y espera `/health/db`. El contenedor SQL usa `ACCEPT_EULA=Y` y `MSSQL_PID=Express`; consulte la [documentación de Microsoft sobre despliegue en contenedores y edición Express](https://learn.microsoft.com/sql/linux/sql-server-linux-docker-container-deployment?view=sql-server-ver17).

El primer acceso es `http://127.0.0.1:5098/admin/login`. Las credenciales de demo no existen en Production. El bootstrap crea sólo tres canales y un administrador; clientes, productos, máquinas y pedidos permanecen vacíos hasta una carga revisada. La hoja privada incluida en el kit conserva los valores confirmados; no se importa automáticamente. Mantenga `datos-privados/` como carpeta hermana de `app/`, fuera del contexto Docker.

## Acceso remoto y configuración

Para un servidor Linux, abra desde su equipo un túnel SSH con su usuario y servidor autorizados:

```bash
ssh -N -L 5098:127.0.0.1:5098 usuario@servidor
```

Después abra la misma URL local. El puerto HTTP no queda abierto a la red. La configuración está destinada a loopback/túnel; una publicación por dominio requiere preparar HTTPS y revisar la configuración de alojamiento por separado.

Antes de la primera inicialización puede elegir un proyecto/puerto propios; deben conservarse después para reutilizar el mismo volumen:

```bash
PRODIMT_PROJECT=prodimt-pilot-otro PRODIMT_HTTP_PORT=5099 bash scripts/pilot/initialize.sh
```

Para guardar secretos fuera del repositorio, exporte `PRODIMT_PILOT_CONFIG_DIR=/ruta/privada` antes de **todos** los comandos. Si cambia el puerto posteriormente, edite únicamente `PRODIMT_HTTP_PORT` en la `.env` privada y actualice la URL de acceso; no regenere secretos ni cambie el nombre del proyecto por accidente.

## Operación, respaldo y actualización

```bash
curl --fail http://127.0.0.1:5098/health/db
bash scripts/pilot/backup.sh
bash scripts/pilot/stop.sh
```

El backup nativo se crea con `COPY_ONLY, CHECKSUM`, se verifica con `RESTORE VERIFYONLY` y se copia a `infra/pilot/backups/` con permisos 600. Con configuración externa queda en `<PRODIMT_PILOT_CONFIG_DIR>/backups/`. Copie el `.bak` y la configuración privada a almacenamiento privado externo al servidor. También queda una copia del `.bak` en el volumen SQL; revise su espacio periódicamente. `VERIFYONLY` verifica el archivo, pero no sustituye un ensayo completo de restauración.

Para actualizar, conserve `.env`, el archivo de acceso y el mismo proyecto/volumen; reemplace sólo los archivos fuente y ejecute `start.sh`. El script guarda la imagen anterior como `<proyecto>-app:previous`, compila, detiene la app, respalda una base existente y aplica explícitamente bootstrap/migraciones antes de arrancar. Programe esa operación fuera de captura: la app queda detenida si falla el respaldo o la migración para poder revisar el error.

```bash
bash scripts/pilot/rollback-app.sh
```

El rollback recupera la imagen anterior sin modificar SQL, datos ni esquema. Requiere que la imagen anterior sea compatible con el esquema vigente; `/health/db` confirma conexión y debe completarse con una comprobación funcional. Un retroceso de esquema o restauración de datos requiere decidir el punto de respaldo y ensayar la recuperación. Al restaurar un `.bak` en otra instancia hay que recuperar la configuración y reprovisionar el login: el siguiente `start.sh` remapea el usuario `prodimt_app` al login para evitar un SID huérfano. No borre volúmenes ni regenere secretos como método de recuperación.

## Separación de permisos y validación

El proceso web usa `prodimt_app`, con `db_datareader` y `db_datawriter`; no usa `sa`, no aplica migraciones y no siembra datos de demostración. El contenedor puntual `bootstrap` recibe la conexión privilegiada, crea/migra el esquema y crea el administrador si falta. Repetir bootstrap conserva datos y contraseña del administrador existente. Ninguna credencial se incorpora a las imágenes.

La CI ejecuta `bash scripts/pilot/smoke-compose.sh` en un proyecto aleatorio desechable. Comprueba build Production, assets y rutas del mismo origen, autenticación, rechazo de demo, ausencia de catálogos demo, permisos SQL sin DDL, bootstrap idempotente, respaldo verificable y persistencia al recrear la app. Sólo ese script elimina su propio volumen de prueba al salir; los scripts operativos conservan los datos. La ejecución local sin Docker sólo permite validar sintaxis, configuración y build Angular; el resultado de CI documenta el ensayo real de contenedores.
