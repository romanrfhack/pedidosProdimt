# Entrega de cobranza para main y despliegue

El usuario autorizó integrar a main y desplegar el 22 de noviembre de 2026. La entrega corresponde al [PR #12](https://github.com/romanrfhack/pedidosProdimt/pull/12). La última instalación acreditada por el usuario es API/frontend `machine-workspace-0d64f5bd2aef`, 21 migraciones; la nueva fuente exige `20261122201338_AddDeliveryCollections`, historia 22.

## Paquete y contrato

El constructor `scripts/pilot/build-collections-package.py` genera `PRODIMT_Cobranza_<main12>.zip` exclusivamente desde HEAD limpio y coincidente con `main`. El backend se publica y Angular se compila desde esa misma fuente, con SQL idempotente 21→22. El paquete incluye `PRODIMT_Instalar_Cobranza.py`, `prodimt-collections-payload.zip`, `PRODIMT_Desplegar_Cobranza_WSL.py`, instrucciones y sumas SHA256. El directorio interior es `prodimt-collections-update`.

La base exacta de ambos releases es `machine-workspace-0d64f5bd2aef`. No se deriva una base permisiva del inventario. Se comprueban los 21 identificadores de migración, fuente y hashes, configuración efectiva, appsettings públicos o sus overrides existentes y destino SQL de PRODIMT. Nunca se empaquetan credenciales ni se modifican claves, tokens o usuarios de inicio de sesión.

Antes del DDL se conserva una referencia privada durable, se detiene exclusivamente PRODIMT y se genera un respaldo `COPY_ONLY,CHECKSUM`, verificado con `RESTORE VERIFYONLY`. Se conservan metadata, índices, conteos y permisos de tablas anteriores, junto con las filas previas de roles/permisos/concesiones y hashes privados de usuarios/asignaciones. Sólo se permiten los anexos de seguridad previstos por la migración. Las cinco tablas financieras requieren SELECT/INSERT permitidos y UPDATE/DELETE denegados al usuario de aplicación.

Después de activar se verifican salud, frontend por HTTPS, endpoints protegidos, esquema y permisos. El reporte final actualiza `migration_ids`, `migrations_after=22` y `backend_hashes` desde SQL y DLL activas. No conserva la historia 21 ni hashes de preflight como resultado final.

Una interrupción anterior al DDL puede devolver los enlaces anteriores. Tras una migración confirmada se recupera hacia delante con exactamente el mismo paquete, sin repetir el DDL ni restaurar automáticamente la base. El marcador conserva la referencia privada necesaria incluso si la API quedó detenida. La repetición correcta devuelve `already_current` después de verificar el release.

La comparación completa de datos y seguridad se acredita antes de activar la API mediante evidencia privada vinculada a fuente, payload, hash del snapshot e historia 22. Si después la API registra operaciones y falla la comprobación de salud, el reintento conserva esas escrituras legítimas y comprueba sólo estructura y permisos SQL, sin exigir los conteos o usuarios anteriores. Evidencia ausente, incompleta o de otra fuente nunca permite omitir la comparación inicial. Una repetición `already_current` tampoco revierte cambios lógicos de roles realizados por administración.

## Validación de esta entrega

- Implementación funcional ya validada con 547 pruebas .NET, 108 SQL Server reales, 68 componentes y 258 navegador. [Detalle](2026-11-22-cobranza.md).
- 32 controles Python nuevos de instalación, payload y WSL correctos. Suite completa 664 casos, cuatro omisiones históricas y cero fallos. Incluyen la regresión de recuperación después de que la API nueva haya registrado auditoría y cambios legítimos de usuario.
- Dos pruebas nuevas en SQL Server 2022 Developer real ejecutan las consultas exactas de esquema, preservación y veinte permisos como usuario de aplicación, y `verify_preservation` con Mostrador nuevo y personalizado. Los casos elevan la suite SQL a 110.
- Los cinco trabajos de CI del PR se verifican antes del merge; el estado vigente se consulta en el PR. No se transforma una validación local en un pase remoto.

## Estado del destino y operación WSL

El intento SSH directo desde este entorno devuelve `Network is unreachable`. Se entrega el ZIP instalable para ejecutarlo desde el WSL del usuario, leyendo el archivo en Descargas de Windows. Después de verificar SHA256 y extraer el ZIP, se ejecuta `python3 PRODIMT_Desplegar_Cobranza_WSL.py --apply` dentro de `prodimt-collections-update`. Primero obtiene inventario y plan; sólo aplica si los pines coinciden. Las credenciales SQL existentes se introducen mediante prompts ocultos en el VPS, sin cambiar el servidor o la base del servicio.

El resultado de destino esperado es `updated/completed`, `migrations_after=22`, salud/esquema/frontend y preservación verificados, `recovery_pending=false`. Hasta observarlo, la instalación de cobranza y su aceptación funcional siguen pendientes. La integración a main, el build o el ZIP no acreditan que el VPS haya cambiado.
