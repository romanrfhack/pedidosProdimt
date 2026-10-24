# Instalación del piloto en pedidos.prodimt.site

Destino elegido: `root@194.238.26.70`. Dominio del piloto: `pedidos.prodimt.site`. La captura de DNS del usuario muestra el registro A correspondiente; su resolución efectiva se comprueba desde el servidor antes de publicar.

La preparación del paquete y sus pruebas no significan que la instalación ya esté realizada. Primero se obtiene el inventario del VPS. Los pasos de escritura se ejecutan después de revisar ese resultado, porque el host contiene otras aplicaciones.

## 1. Inventario de sólo lectura desde WSL

Descargue `PRODIMT_Preflight_VPS.py` en la carpeta Descargas de Windows. Desde WSL:

```bash
ssh root@194.238.26.70 'python3 -' \
  < /mnt/c/Users/TUF/Downloads/PRODIMT_Preflight_VPS.py
```

El script se ejecuta desde la entrada estándar y no se copia al VPS. No instala paquetes, no reinicia servicios, no cambia DNS/Nginx y no imprime secretos ni configuraciones completas. El JSON indica herramientas, memoria disponible, discos, puertos, dominio, posibles conflictos de Nginx y si ya existe una instalación del piloto.

En este punto, compartir el JSON permite preparar el siguiente paso con la configuración real. Debe comprobarse:

- Linux x86-64, Docker operativo y Compose v2 con soporte `up --wait`.
- Al menos 5 GiB de **RAM disponible** para la primera compilación, además de la carga de las aplicaciones existentes; no basta con la RAM total del servidor.
- Al menos 10 GiB libres tanto en `/opt` como en el almacenamiento real de Docker. Reservar más espacio para respaldos y crecimiento.
- Puerto 5098 libre o perteneciente a la instalación existente del piloto. Los puertos 80/443 normalmente pertenecen al Nginx existente y no se liberan ni se sustituyen.
- Nginx existente activo; un virtual host ajeno que cubra `pedidos.prodimt.site`, un comodín o una expresión regular exige revisar el conflicto antes de aplicar.
- Certbot/OpenSSL disponibles y mecanismo de renovación existente identificado. Si falta una herramienta, resolverlo según la distribución real; estos scripts no ejecutan actualizaciones globales ni reinstalan Docker.
- A de `pedidos.prodimt.site` hacia `194.238.26.70`; no publicar con un AAAA que apunte a otra máquina. El script de publicación de esta versión admite sólo la IPv4 elegida.

## 2. Copiar y extraer la primera instalación

Sólo después de revisar el inventario. El kit esperado es `PRODIMT_Piloto_I04_20261024.zip`, con raíz `prodimt-piloto/`, fuente en `app/` y datos reales en `datos-privados/` fuera del contexto Docker.

Desde WSL:

```bash
scp /mnt/c/Users/TUF/Downloads/PRODIMT_Piloto_I04_20261024.zip \
  root@194.238.26.70:/root/
ssh root@194.238.26.70
```

Dentro de la sesión SSH del VPS, ejecutar el bloque completo:

```bash
bash <<'BASH'
set -euo pipefail
umask 077
if [[ -e /opt/prodimt-piloto ]]; then
  printf 'Ya existe /opt/prodimt-piloto. Revisar actualización; no se sobrescribió nada.\n' >&2
  exit 1
fi
python3 -m zipfile -e /root/PRODIMT_Piloto_I04_20261024.zip /opt
cd /opt/prodimt-piloto
sha256sum --status -c SHA256SUMS.txt
find app -type d -exec chmod 755 {} +
find app -type f -exec chmod 644 {} +
chmod 700 datos-privados
printf 'Paquete extraído y archivos verificados.\n'
BASH
```

Si la carpeta ya existe, no borrarla ni extraer encima. Se conserva su `.env`, acceso privado, datos y volumen; una actualización se prepara contra esa instalación concreta. La validación de hashes detecta alteraciones accidentales y no sustituye obtener el paquete del origen correcto.

La normalización de permisos se limita a la fuente en `app/`, para que la compilación en Docker pueda leer los archivos extraídos. Los scripts se invocan mediante `bash` o `python3` y no requieren permiso de ejecución. `datos-privados/` conserva directorios 700 y archivos 600 por la máscara de extracción; la configuración privada se genera después, fuera de `app/`, con esos mismos permisos restrictivos.

## 3. Inicializar SQL y la aplicación en loopback

En el VPS:

```bash
bash <<'BASH'
set -euo pipefail
cd /opt/prodimt-piloto/app
export PRODIMT_PILOT_CONFIG_DIR=/opt/prodimt-piloto/config
bash scripts/pilot/initialize.sh
bash scripts/pilot/preflight.sh
bash scripts/pilot/start.sh
curl --fail --silent --show-error http://127.0.0.1:5098/health/db
BASH
```

Los secretos se generan una sola vez en `/opt/prodimt-piloto/config`, separados de la fuente. SQL usa su volumen propio y no abre 1433 al host. La aplicación queda en `127.0.0.1:5098`; todavía no es pública. No hay clientes ni productos demo; los datos privados no se cargan automáticamente.

Para comprobar el acceso antes de HTTPS, abrir otra terminal WSL y mantener este túnel activo:

```bash
ssh -N -L 5098:127.0.0.1:5098 root@194.238.26.70
```

Abrir `http://127.0.0.1:5098/admin/login`. El acceso administrativo está en `/opt/prodimt-piloto/config/ACCESO-PRIVADO.txt`; consultarlo únicamente en la sesión privada del servidor y no pegarlo en el chat. Mantener una copia privada de ese archivo y `.env`.

## 4. Preparar HTTP sólo para el certificado

En el VPS:

```bash
cd /opt/prodimt-piloto/app
python3 scripts/pilot/publish-nginx.py
python3 scripts/pilot/publish-nginx.py --apply-http
```

El primer comando muestra el plan sin cambiar archivos. El segundo revisa Nginx/DNS, crea únicamente `/etc/nginx/sites-available/prodimt-pedidos.conf` y su enlace propio, comprueba la configuración y recarga Nginx. Cualquier archivo ajeno o plantilla modificada se conserva y bloquea la operación.

En esta fase se sirve sólo `/.well-known/acme-challenge/`; el resto responde 503. No se publica el formulario de acceso mediante HTTP. Si falla la comprobación del challenge, se restaura el virtual host anterior del piloto.

Desde WSL, comprobar que el puerto 80 es accesible externamente:

```bash
curl --noproxy '*' --max-time 15 -I http://pedidos.prodimt.site/
```

El resultado esperado antes de HTTPS es **503**. Un timeout, otro dominio o una página ajena requiere revisar DNS, firewall y virtual hosts. No abrir 5098 ni 1433 para resolverlo.

## 5. Certificado y HTTPS

En el VPS, indicar el correo de contacto real. La bandera `--accept-terms` expresa aceptación del acuerdo del proveedor ACME y se requiere cuando hace falta emitir un certificado:

```bash
cd /opt/prodimt-piloto/app
read -r -p 'Correo para el certificado: ' prodimt_cert_email
python3 scripts/pilot/publish-nginx.py --apply-https \
  --email "$prodimt_cert_email" --accept-terms
unset prodimt_cert_email
```

Se usa `certbot certonly --webroot`, limitado al nombre de certificado `pedidos.prodimt.site`; no se usa el instalador que reescribe los virtual hosts. Antes de activar HTTPS se exige salud de la aplicación en loopback y se revisa que cualquier certificado existente corresponda sólo a este dominio y a este método de renovación.

Tras emitir/reutilizar el certificado, el sitio HTTP redirige a HTTPS y Nginx envía la aplicación a `127.0.0.1:5098`. La plantilla previa del piloto se respalda en `/var/backups/prodimt-nginx`. Un fallo de activación restaura el virtual host y hook previos; un certificado ya emitido se conserva. Los scripts no eliminan ni revocan certificados.

Desde WSL:

```bash
curl --fail --silent --show-error --noproxy '*' --max-time 15 \
  https://pedidos.prodimt.site/health/db
```

Después abrir `https://pedidos.prodimt.site/admin/login`, iniciar sesión, comprobar configuración de un cliente y registrar una prueba ficticia controlada antes de cargar operación real. La salud de base acredita conectividad; no sustituye la comprobación funcional.

## 6. Renovación, respaldo y continuidad

Confirmar el programador de Certbot que mostró el inventario, sin duplicarlo. Ensayar únicamente la renovación del certificado del piloto:

```bash
certbot renew --cert-name pedidos.prodimt.site --dry-run
```

El hook propio recarga Nginx sólo cuando la renovación corresponde al certificado del piloto. La prueba de renovación usa la infraestructura de pruebas ACME; su éxito no significa que una renovación real ya haya ocurrido.

Para respaldar SQL:

```bash
cd /opt/prodimt-piloto/app
export PRODIMT_PILOT_CONFIG_DIR=/opt/prodimt-piloto/config
bash scripts/pilot/backup.sh
```

Copiar el backup verificado y los archivos privados a un destino privado externo al VPS. Antes de actualizar, leer [README.md](README.md): conservar configuración/proyecto/volumen y no extraer otro kit encima de datos privados. El rollback de imagen no retrocede el esquema SQL ni deshace datos.

## Referencia técnica

Las opciones de webroot, emisión y renovación se contrastaron con la [guía oficial de Certbot](https://eff-certbot.readthedocs.io/en/stable/using.html) y su [referencia de comandos](https://eff-certbot.readthedocs.io/en/stable/man/certbot.html). La instalación efectiva, resolución pública, reglas de firewall y emisión de certificado siguen pendientes hasta ejecutar estos pasos en el VPS.
