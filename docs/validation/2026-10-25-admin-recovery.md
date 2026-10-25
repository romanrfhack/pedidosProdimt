# Recuperación local de acceso web — 25 de octubre de 2026

## Alcance y evidencia

I04 está desplegado y saludable según la ejecución del usuario. El navegador carga el login y rechaza credenciales; la captura SQL muestra que la cuenta web `admin` está activa. El usuario autoriza restablecer acceso y probarlo. Se prepara recuperación puntual por SSH; no una función pública de recuperación ni una reinstalación.

`scripts/pilot/reset-native-admin.py`, revisión `20261025-admin-reset1`, apunta sólo al servicio/release I04 y la base/puerto nativos revisados. El modo plan no escribe ni solicita secretos. Para aplicar exige root y terminal con getpass sin fallback visible, cuenta SQL existente, cuenta web única/activa y esquema I04. No lee ni modifica api.env ni otros secretos de la aplicación.

## Contrato

- Nueva contraseña web repetida y de 16–128 caracteres; formato de ASP.NET Identity V3, HMAC-SHA512, 100,000 iteraciones, sal aleatoria de 16 bytes y subclave de 32 bytes. Se contrasta con el servicio .NET real mediante fixture sintética; fuente del formato: https://source.dot.net/Microsoft.Extensions.Identity.Core/PasswordHasher.cs.html.
- Contraseña SQL sólo en entorno limpio del proceso sqlcmd; script SQL por stdin, `-I`, `-x`, `-b` y opciones SET explícitas. Contraseña web no viaja a SQL; cadenas SQL como hex UTF-16 evitan interpolación de comillas o comandos. No hay secretos en argumentos o diagnóstico.
- Lock compartido con la actualización nativa. Respaldo de la fila anterior con hash en directorio nuevo root/700 y archivo 600, antes de modificar; no se guarda la nueva contraseña ni su hash en ese respaldo. El hash anterior también es privado.
- Cambia únicamente PasswordHash de la fila observada por Id/nombre/estado activo/hash exacto. Inserta auditoría System en la misma transacción con referencia única, sin datos de credenciales. Preserva los otros campos/usuarios, incluyendo inactivos. Un conflicto o fallo de auditoría revierte la actualización de la fila.
- Login por HTTPS y consulta GET /api/admin/users con el JWT sólo en stdin. Certificado validado y virtual host fijado a loopback del VPS; sin tokens/cuerpos privados en salida. No hay reinicio ni cambio en cuentas SQL, permisos, claves JWT, catálogo o pedidos.
- Resultado registra fase, recuperación realizada, códigos HTTP y acceso confirmado. Un fallo tras enviar DML conserva la marca de posible escritura y referencia; no restaura ni vuelve a intentar automáticamente. El cambio no revoca JWT ya emitidos, cuyo comportamiento permanece igual.

## Validación

Pendiente registrar resultados finales de Python, interoperabilidad .NET y transacción en SQL Server desechable. Todos los datos de pruebas son ficticios. No se ha ejecutado el reset en el VPS ni se declara el acceso recuperado hasta recibir su JSON final y la comprobación de navegador.
