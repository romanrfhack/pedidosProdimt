# I05 — Cambio de contraseña propia y actualización nativa

El usuario compartió resultado exitoso de creación de su cuenta: fila/auditoría verificadas, login y consulta administrativa HTTP 200. Su contraseña no se conserva en este documento ni en el repositorio. Se ejecuta la autorización previa de añadir cambio de contraseña propia.

Código funcional `304a0cda39bacefa72f41533e99cb7beb22af11f`; [CI 40409593142](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40409593142), cuatro jobs correctos: 212 .NET (5 dominio, 207 aplicación), 24 SQL Server reales, 55 E2E, 129 Python y smoke Production/persistencia/respaldo. Build Angular local correcto. La descarga local de Chromium estuvo truncada; las pruebas de navegador se ejecutaron correctamente en CI.

- POST `/api/admin/account/change-password` exige AdminAccess y toma el Id sólo del JWT. Nueva contraseña de 16–128 caracteres, confirmación exacta y contraseña actual verificada. No acepta cambiar otra cuenta por datos extra del cuerpo.
- UPDATE condicional por Id/estado activo/hash observado, con comparación binaria y auditoría en una transacción. Concurrencia: un ganador y una auditoría; fallo al insertar auditoría revierte el hash. No se añaden ni cambian tablas/columnas/migraciones.
- JWT administrativo con sello HMAC del hash, comparado contra la cuenta activa por solicitud. Cambiar contraseña o desactivar invalida sesiones anteriores; los tokens de cliente permanecen independientes. Los JWT anteriores a I05 sin sello requieren nuevo login al instalar.
- Pantalla `/admin/cambiar-contrasena`, campos password con autocomplete apropiado, confirmación y bloqueo de doble envío. Éxito limpia credenciales/sesión y vuelve al login. Un 401 antiguo no cierra un token más reciente de la misma instancia.

## Instalación preparada

`build-password-package.py` empaqueta API framework-dependent linux-x64 y Angular Production ya compilados; genera manifiesto, checksum y un instalador fijado por hash de payload/source. El artefacto no contiene secretos del servidor ni de la cuenta nueva.

`install-password-update.py` permite sólo el I04 conocido en servicio/release actual. Antes de escribir verifica paquete/rutas, proceso, Production sin seed/migración automática, destino SQL exacto e historial de nueve migraciones/permisos de la cuenta cotidiana para leer/actualizar AdminUsers e insertar AuditLogs. Consultas SQL son sólo de lectura. Preserva configuración actual de appsettings y api.env y usa snapshot privado; configura archivos copiados con permisos 640/root:www-data.

Staging y archivos se contrastan completos antes de activar; enlaces se cambian bajo lock compartido y sólo se reinicia PRODIMT. Verifica proceso/DLL, base, protección del endpoint y bytes de HTML/JS/CSS por HTTPS. Recupera ambos enlaces y salud anterior si falla tras parar. No ejecuta DDL, restaura SQL, cambia Nginx ni cambia contraseñas automáticamente. 135 pruebas Python locales correctas, incluidas seis nuevas de empaquetado/guardas/reversión; graphify no disponible (127). La validación final del instalador y artefacto se registra abajo.

La aplicación funcional está probada e I05 quedó instalado según el resultado remoto registrado abajo. La operación final de contraseña la hará el usuario desde la pantalla después de volver a iniciar sesión. No repetir el script de alta: su referencia inicial se volverá obsoleta tras cambiar la contraseña.

## Cierre técnico y artefacto entregado

Fuente final `886bee7b2202b399ee063fe65d524b7f444e205c`; [CI 40411669397](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40411669397) con los cuatro jobs correctos: 212 .NET, 24 SQL Server, 55 navegador y 135 Python, contrato CLI real y smoke Production/persistencia/respaldo. La corrida anterior del instalador 40411116536 también cerró sus cuatro jobs correctamente. La documentación posterior no cambia binarios ni requiere repetir esas suites.

Artefacto `PRODIMT_Cambio_Contrasena_I05.zip`, 5,591,118 bytes; SHA256 `03a993f8d07691ed1813cd8f5c76625b8328d9c5b55891c9f3cd7a2739c59525`. Payload SHA256 `675c953c22db5cdd039dae5381779fd3520029fd6fbcf0e27195b34003adcb1e`. Archivo guardado y metadatos verificados. Al extraer, los únicos archivos enviados a `/root/` son `PRODIMT_Instalar_I05.py` y `prodimt-password-payload.zip`, nombres específicos para evitar sobrescribir archivos genéricos de otros proyectos.

Se verificaron checksums exteriores, manifiesto completo de 54 archivos mediante el instalador fijado, sourceCommit exacto y ejecución sin `--apply`: `status: planned`, `changes_made: false`. Compilación framework-dependent linux-x64 con SDK 10.0.403 y frontend Production con base API vacía. No se usaron datos ni contraseñas reales para pruebas o empaquetado.

Pendiente: cambio desde el navegador e inicio con la nueva contraseña. La instalación ya fue ejecutada por el usuario desde WSL; no hubo SSH directo desde este entorno. La cuenta actual sigue siendo la del alta verificada; no volver a ejecutar ese script después del cambio de contraseña.

## Resultado remoto recibido — 26 de octubre de 2026

El usuario verificó el SHA256 exterior, extrajo el ZIP, verificó checksums interiores y ejecutó `PRODIMT_Instalar_I05.py --package /root/prodimt-password-payload.zip --apply` desde WSL/SSH. Resultado `20261026-password-install1`: `status: updated`, `phase: completed`, `changes_made: true`, `health_verified: true`.

- Código activo `886bee7b2202b399ee063fe65d524b7f444e205c`.
- Backend `/var/www/prodimt-pedidos/backend/releases/password-886bee7b2202`.
- Frontend `/var/www/prodimt-pedidos/frontend/releases/password-886bee7b2202`.
- Historial de nueve migraciones, I04 presente y permisos read_admin/update_admin/write_audit verificados; `database_migrations_run: false`.
- Snapshot privado `/var/backups/prodimt-pedidos/password-update-20261026T231120Z-318fd2a6fc3f`.
- `password_changed: false` esperado: instalar no rota credenciales. Falta que el usuario ejecute el cambio desde la pantalla y confirme el login posterior.

El resultado acredita las verificaciones de salud del instalador, no todavía el uso del formulario por el usuario ni la carga privada/ensayo operativo. Se actualizan únicamente documentos; se conserva la evidencia técnica anterior sin repetir suites. PR en borrador y main sin fusión.
