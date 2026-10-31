# Recuperación local de acceso web — 25 de octubre de 2026

## Alcance y evidencia

I04 está desplegado y saludable según la ejecución del usuario. El navegador muestra un error de credenciales y la captura SQL muestra `admin` activo. El usuario autoriza restablecer acceso y probarlo. Una captura posterior identifica 404 por ruta duplicada, no un rechazo confirmado de contraseña; ver diagnóstico al final. La herramienta es recuperación puntual por SSH, no una función pública ni una reinstalación.

`scripts/pilot/reset-native-admin.py`, revisión `20261025-admin-reset1`, apunta sólo al servicio/release I04 y la base/puerto nativos revisados. El modo plan no escribe ni solicita secretos. Para aplicar exige root y terminal con getpass sin fallback visible, cuenta SQL existente, cuenta web única/activa y esquema I04. No lee ni modifica api.env ni otros secretos de la aplicación.

## Contrato

- Nueva contraseña web repetida y de 16–128 caracteres; formato de ASP.NET Identity V3, HMAC-SHA512, 100,000 iteraciones, sal aleatoria de 16 bytes y subclave de 32 bytes. Se contrasta con el servicio .NET real mediante fixture sintética; fuente del formato: https://source.dot.net/Microsoft.Extensions.Identity.Core/PasswordHasher.cs.html.
- Contraseña SQL sólo en entorno limpio del proceso sqlcmd; script SQL por stdin, `-I`, `-x`, `-b` y opciones SET explícitas. Contraseña web no viaja a SQL; cadenas SQL como hex UTF-16 evitan interpolación de comillas o comandos. No hay secretos en argumentos o diagnóstico.
- Lock compartido con la actualización nativa. Respaldo de la fila anterior con hash en directorio nuevo root/700 y archivo 600, antes de modificar; no se guarda la nueva contraseña ni su hash en ese respaldo. El hash anterior también es privado.
- Cambia únicamente PasswordHash de la fila observada por Id/nombre/estado activo/hash exacto. Inserta auditoría System en la misma transacción con referencia única, sin datos de credenciales. Preserva los otros campos/usuarios, incluyendo inactivos. Un conflicto o fallo de auditoría revierte la actualización de la fila.
- Login por HTTPS y consulta GET /api/admin/users con el JWT sólo en stdin. Certificado validado y virtual host fijado a loopback del VPS; sin tokens/cuerpos privados en salida. No hay reinicio ni cambio en cuentas SQL, permisos, claves JWT, catálogo o pedidos.
- Resultado registra fase, recuperación realizada, códigos HTTP y acceso confirmado. Un fallo tras enviar DML conserva la marca de posible escritura y referencia; no restaura ni vuelve a intentar automáticamente. El cambio no revoca JWT ya emitidos, cuyo comportamiento permanece igual.

## Validación

120/120 pruebas Python locales y remotas correctas. [CI final 40213743976](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40213743976), commit `fe0f19d37f11216eacc56246f93b85db8f51fb62`: cuatro jobs correctos, 205 pruebas .NET (incluidas las dos de interoperabilidad con el PasswordHashService real), 21 SQL, 53 E2E y smoke Production/persistencia/respaldo.

El contrato sqlcmd usa el transporte real del resetter dentro de una base desechable: cambia únicamente el hash de la cuenta seleccionada, conserva campos/usuarios inactivos/clientes/pedidos, guarda una auditoría sin credenciales, rechaza el hash observado obsoleto y revierte la contraseña cuando un trigger de prueba fuerza el fallo de auditoría. La corrida inicial `40213557895` confirmó .NET y Python/Production; su adaptador SQL usaba el transporte de migración sin ancho de salida para JSON por stdin y falló antes del reset. Se cambió sólo el adaptador para ejercitar la CLI propia del resetter; el entregable conserva exactamente los mismos bytes.

Artefacto `PRODIMT_Restablecer_Admin.py`: 15,670 bytes, SHA256 `5072118445363332090066bae939ec3977539a95a997008a02f1dc52e7111a15`, copia exacta del script probado. `git diff --check` correcto; graphify no instalado (127). Todos los datos de pruebas son ficticios. El intento remoto se registra abajo; todavía no se declara acceso recuperado.

## Intento recibido y diagnóstico de ruta

El usuario ejecutó la revisión exacta. Se detuvo en `new-web-password` con `password_confirmation_mismatch`, `changes_made: false` y `password_reset: false`, antes de respaldar/modificar la cuenta. No cambió la contraseña anterior. Se pausa la recuperación al recibir una captura de Network: POST `https://pedidos.prodimt.site/api/api/auth/admin/login`, HTTP 404. Ese error no prueba credenciales inválidas; recomendar el reset antes de comprobar la petición fue prematuro.

Comprobación HTTPS directa desde este entorno, sin SSH ni contraseñas reales:

- POST `/api/auth/admin/login` con identidad ficticia inexistente: 401 y error de autenticación esperado.
- POST `/api/api/auth/admin/login` con esa misma entrada: 404.
- HTML publicado de 888 bytes, SHA256 `23f19ca4f7b80f266a8185ed00c1cbc0acf8a56ab49247443d3f53d3afd7a8df`, referencia `main-ZDENE75O.js`.
- Bundle de 439,157 bytes, SHA256 `1412bf3a6e27a5537844f3fce1182b080be798081fea2dd092a8a719744ebb02`: contiene `apiBaseUrl:""` y una referencia `/api/auth/admin/login`, coincidiendo con Production I04.

La diferencia entre la petición del usuario y el JavaScript publicado apunta a una versión anterior en caché. Es una inferencia; falta recargar sin caché/incógnito y comprobar la petición nueva. No se modifican Nginx, releases ni contraseñas por esa observación. Primero probar la ruta correcta con la contraseña anterior; usar el reset preparado sólo si esa petición devuelve 401 con sus credenciales.
