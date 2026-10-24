# Actualización nativa I03-B

El VPS elegido ya tiene PRODIMT activo mediante systemd, frontend/backend en releases, SQL Server nativo y certificado con renovación webroot. Actualizar esa instalación conserva su base y evita compilar o crear otro SQL en un host compartido.

## Paquete preparado

API .NET 10 framework-dependent linux-x64 y Angular Production compilados fuera del VPS desde `b630072ce67a691c3757d0590cc7f7d45f423d52`, el mismo código validado en CI I04. El frontend usa la API del mismo origen y sus credenciales demo están vacías. Incluye SQL idempotente con nueve migraciones, cinco anteriores y cuatro de octubre. Los binarios no incluyen datos reales, archivos privados del servidor ni credenciales operativas.

La comprobación de API `/health` con configuración Production y conexión ficticia acredita arranque, no conectividad con la base del usuario. Las veinte pruebas SQL de CI usan una base desechable y no prueban su esquema actual.

## Secuencia

1. Ejecutar la inspección nativa de sólo lectura con el servicio actual encendido. Lee configuración efectiva y muestra sólo metadatos permitidos, flags de compatibilidad y resultados SQL de consulta.
2. Revisar destino de conexión, migraciones aplicadas, posibles duplicados de la clave nueva y permisos de respaldo/verificación y DDL. La cuenta cotidiana puede requerir otra identidad para la actualización; no elevarla permanentemente.
3. Preparar respaldo `COPY_ONLY` con checksum y verificarlo antes de aplicar migraciones. Guardar versión de configuración y destinos actuales de los releases en un directorio privado. El éxito de VERIFYONLY no sustituye un ensayo de restauración.
4. Preparar nuevos releases y sus permisos sin cambiar `current`. Programar una ventana breve, detener sólo PRODIMT, aplicar las migraciones que faltan y cambiar enlaces. Mantener secretos en la configuración privada existente. No ejecutar el bootstrap de instalación nueva, que puede crear usuarios/canales.
5. Reiniciar únicamente el servicio PRODIMT y comprobar salud, login, catálogos, pedidos e I04. Ante fallo, restaurar enlaces de binarios y servicio anterior; el rollback de código no deshace el esquema SQL.

La inspección y el paquete son preparación. La fase de escritura queda pendiente hasta revisar su resultado y concretar respaldo y actualización para la instalación observada. Nginx y certificado actuales se conservan durante esta fase.

## Referencias

- [Despliegue de migraciones EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).
- [RESTORE VERIFYONLY y permisos](https://learn.microsoft.com/en-us/sql/t-sql/statements/restore-statements-verifyonly-transact-sql).
