# I06 — Catálogo de clientes

Código y herramientas de entrega: `965dc65d82d2322b8c897a164c592bad6ed975a9`, rama `codex/pilot-foundation-2026-10-24`, PR #1 en borrador, sin fusión. El usuario confirmó que ya importó los clientes. La preparación no accedió por SSH al VPS; el usuario instaló después desde WSL y compartió el resultado que se registra abajo.

## Resultado

Búsqueda principal y filtros, alta/edición en modal centrado y diferido, tarjetas ámbar por confirmar, verdes con pedido y grises por NoOrder o descanso al final. Etiquetas explican el estado sin depender del color. Los pedidos pendientes de revisión lo indican. La fecha es operativa de Ciudad de México; actualización cada 30 segundos, al volver y manual, conservando el formulario abierto. Una consulta fallida no inventa confirmaciones y conserva el resultado anterior con advertencia/fecha.

Descansos semanales explícitos por cliente, ISO lunes=1 a domingo=7. Sin configuración inicial; un pedido vigente prevalece. El descanso no desactiva ni crea NoOrder/demanda. Omitir RestDays en la API/CSV existente conserva el dato; [] limpia. Auditoría con anterior/nuevo, sin publicar datos privados.

## Verificación

[CI 40710267512](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40710267512): cuatro jobs correctos.

- .NET: 225 pruebas (5 dominio, 220 aplicación), modelo/migración consistentes.
- SQL Server: 25 pruebas HTTP/transaccionales, persistencia entre hosts y compatibilidad del CSV.
- Navegador: 60 pruebas, incluidas cinco de modal/estados/descansos/errores/móvil y protección de doble envío. Revisión visual local con datos ficticios en escritorio y 390 px.
- Python: 148 pruebas, incluyendo hashes, rutas, respaldo antes de DDL, fallo de respaldo sin migración, reintento desde historial diez, secretos fuera de argv y configuración Production adicional conservada.
- ODBC sqlcmd real: incremental nueve→diez repetible, opciones de sesión explícitas, rechazo de columna parcial/constraint no confiable, rollback de transacción fallida, valor inicial cero y conteos preservados. Se conserva también la prueba original cinco→nueve.
- Smoke Docker Production correcto, sin datos reales, instancia desechable eliminada.
- Build Angular Production: 453.15 kB iniciales; catálogo y modal en chunks diferidos. Publicación .NET linux-x64 framework-dependent: requiere 10.0.0, compatible con runtimes 10.0.13 inventariados.

## Paquete

`PRODIMT_Catalogos_I06.zip`, 5,645,785 bytes.

SHA256 exterior: `8d733fe140f369dc810f9d634d4a1ee2f8e3b31b5451dd645eda193ec9dd6fcc`.

Payload SHA256: `f301b87547980e4ea37286abc2318c8a601f80acc630641bdaa665803a471f8f`.

ZIP raíz `prodimt-catalog-update/`: `PRODIMT_Instalar_I06.py`, `prodimt-catalog-payload.zip`, `LEEME.txt`, `SHA256SUMS.txt`. CRC, sumas, manifiesto y carga del instalador fijado verificados; 60 archivos. Sin secretos ni clientes reales en paquete/repositorio.

## Procedimiento de aplicación

Desde WSL verificar ZIP/checksums, copiar sólo los dos archivos del instalador/payload a `/root/` y ejecutar por SSH **-t desde terminal interactiva**:

```bash
ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I06.py --package /root/prodimt-catalog-payload.zip --apply'
```

Solicita administrador SQL existente, oculto y sólo en memoria; no es el usuario web. Comprueba proceso/links I05 `password-886bee7b2202`, Production y permisos antes de parar. Snapshot privado, respaldo COPY_ONLY/CHECKSUM/VERIFYONLY antes de migrar `20261028142346_AddCustomerRestDays`, historial exacto nueve/diez, conteos y DML comprobados. Cambia links a `catalog-965dc65d82d2`, verifica proceso, salud DB, endpoints protegidos y todos los JS/CSS, incluidos chunks diferidos, mediante HTTPS local. No modifica Nginx, cuentas, contraseñas ni otros servicios.

Si falla tras detener, intenta restaurar links/aplicación anterior y comprobar salud; nunca restaura SQL automáticamente. Una migración completa con I05 de nuevo activo permite reintento tras otro respaldo. No repetir si I06 ya quedó activo. Esperar `status: updated`, `migrations_after: 10`, `health_verified: true`; después Ctrl+F5 y aceptación de interfaz con datos reales. La validación técnica y preparación del ZIP no equivalen a instalación ni a aprobación del calendario de cada cliente.

## Resultado de instalación confirmado por el usuario

El 28 de octubre el usuario ejecutó el paquete y compartió el JSON de revisión `20261028-catalog-install1`:

- `status: updated`, `phase: completed`, `health_verified: true`.
- `migrations_before: 9`, `migrations_after: 10`, `database_migrations_run: true`.
- `migrated_schema_verified: true`, `existing_data_counts_preserved: true`.
- `password_changed: false`, `secret_values_printed: false`, `database_automatically_restored: false`.
- `source_commit: 965dc65d82d2322b8c897a164c592bad6ed975a9`.
- Backend: `/var/www/prodimt-pedidos/backend/releases/catalog-965dc65d82d2`.
- Frontend: `/var/www/prodimt-pedidos/frontend/releases/catalog-965dc65d82d2`.
- Snapshot: `/var/backups/prodimt-pedidos/catalog-update-20261028T180203Z-23dae09a3d30`.
- Respaldo SQL verificado: `/var/opt/mssql/backup/prodimt-i06-20261028T180203Z-23dae09a3d30/ProdimtPedidos-copy-only.bak`.

El `migrations_count: 9` de `database_checks` corresponde al preflight; el resultado final es diez. Los conteos verificados no equivalen a una conciliación individual de los datos. Falta aceptación visual del usuario tras Ctrl+F5 y programación de descansos conocidos. No repetir instalación/bootstrap/importación para esa revisión. Se conserva la validación técnica del código entregado; esta actualización sólo documenta evidencia de despliegue aportada por el usuario.
