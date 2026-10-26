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

La aplicación funcional está probada, pero I05 todavía no está instalado. La operación final de contraseña la hará el usuario desde la pantalla después de ejecutar la actualización nativa y volver a iniciar sesión. No repetir el script de alta: su referencia inicial se volverá obsoleta tras cambiar la contraseña.

## Cierre técnico y artefacto entregado

Fuente final `886bee7b2202b399ee063fe65d524b7f444e205c`; [CI 40411669397](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40411669397) con los cuatro jobs correctos: 212 .NET, 24 SQL Server, 55 navegador y 135 Python, contrato CLI real y smoke Production/persistencia/respaldo. La corrida anterior del instalador 40411116536 también cerró sus cuatro jobs correctamente. La documentación posterior no cambia binarios ni requiere repetir esas suites.

Artefacto `PRODIMT_Cambio_Contrasena_I05.zip`, 5,591,118 bytes; SHA256 `03a993f8d07691ed1813cd8f5c76625b8328d9c5b55891c9f3cd7a2739c59525`. Payload SHA256 `675c953c22db5cdd039dae5381779fd3520029fd6fbcf0e27195b34003adcb1e`. Archivo guardado y metadatos verificados. Al extraer, los únicos archivos enviados a `/root/` son `PRODIMT_Instalar_I05.py` y `prodimt-password-payload.zip`, nombres específicos para evitar sobrescribir archivos genéricos de otros proyectos.

Se verificaron checksums exteriores, manifiesto completo de 54 archivos mediante el instalador fijado, sourceCommit exacto y ejecución sin `--apply`: `status: planned`, `changes_made: false`. Compilación framework-dependent linux-x64 con SDK 10.0.403 y frontend Production con base API vacía. No se usaron datos ni contraseñas reales para pruebas o empaquetado.

Pendiente: ejecución del usuario desde WSL, JSON `updated`/salud y cambio desde el navegador. No se ha conectado por SSH ni modificado el VPS desde este entorno. La cuenta actual sigue siendo la del alta verificada; no volver a ejecutar ese script después del cambio de contraseña.
