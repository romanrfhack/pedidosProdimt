# I22 revisión 4 — Compatibilidad con el rol personalizado existente

## Evidencia confirmada

El diagnóstico de semillas de SHA `91176603ecda19fdb5aaae5d3141eefd3e006bd15cb991dc5720da57f740eb45` terminó observed, lectura y pines verificados, historia16 y salud correctas. driver_role: different_id_same_label=1, exists_expected_id=0, label_active=1, label_system=0, grants_count=2, user_assignments_count=1. Las tres semillas de permisos devolvieron todos los conteos en0.

Existe, por tanto, un rol personalizado con el nombre que la migración pretendía insertar. No hay que borrar ni adoptar sus asignaciones. La aplicación anterior está recuperada según el reporte previo; I22 aún no está instalado. La validación SQL real del intento anterior pasó en bases limpias pero no incluía esa personalización.

## Cambio mínimo

Se mantiene el ID canónico `22220000-0000-0000-0000-000000000001`, con nombre estable `Repartidor - Camioneta (sistema)`. La aplicación resuelve acceso por permisos efectivos; no depende de ese nombre. El rol existente no se renombra, convierte, desactiva ni recibe o pierde permisos. Sus usuarios no se trasladan ni reciben el nuevo rol automáticamente.

El instalador v4 deriva el SQL nativo del mismo payload verificado y adapta únicamente el literal Name del INSERT SecurityRoles esperado, conservando el escape dentro de EXEC(N'...'). DDL, migración17, IDs, claves, cantidades, aplicación, frontend y runner siguen siendo los mismos. El nombre de presentación es una adaptación de compatibilidad del instalador, no un cambio a una migración anterior publicada I18/I19 ni una reescritura del historial.

Se verifican cuatro grupos de colisión: ID del rol, nuevo nombre, IDs de permisos y claves de permisos. El nombre del rol personalizado original queda permitido y conservado. Una ocupación del nuevo nombre o de las nuevas identidades detiene la actualización antes de cambios operativos. Hay comprobaciones DML en preflight, administrativas y próximas al DDL. No se reduce la protección de esquema, configuración, claves o seguridad.

## Validación SQL con personalización

El helper de validación añade un parámetro opcional baseline_fixture. El instalador sólo adapta el helper antiguo de SHA exacto y los dos fragmentos esperados; el helper resultante debe tener el SHA de la versión fuente revisada. No hay adaptación de código arbitrario ni aceptación de layouts distintos.

El callback únicamente acepta el nombre generado ProdimtI22Validation_<32hex> que coincida con transport.database, nunca master, ProdimtPedidos o bases del runner. Crea allí datos ficticios: rol personalizado con el nombre original, dos grants baseline y un usuario asignado, antes de tomar fingerprints. Se prueban el SQL nativo corregido, rollback, esquema estricto, permisos DML, preservación de seguridad y repetición; la base temporal debe eliminarse antes de tocar producción.

El fingerprint de RolePermissions se fortalece: sólo excluye los nuevos grants previstos del Administrador y del rol canónico. Para cualquier otro rol incluye todas las claves, incluidas delivery.*, de modo que una concesión accidental al rol personalizado no puede quedar oculta. El fingerprint completo de UserRoles detecta asignaciones nuevas no previstas. Se exige también nombre canónico, dos permisos propios exactos y ninguna asignación al nuevo rol durante el seed; reintentos con una instalación ya vigente admiten las asignaciones legítimas posteriores.

## Entrega y aceptación

Se entrega PRODIMT_Instalar_I22_v4.py y se actualizan las instrucciones del ZIP completo, conservando el payload byte por byte. La aplicación compilada sigue fijada a 0cfe3ef511d80e4af032a3e64abd97391e86df08 y el payload a fb0485190d68285cc121603a3b946a19fb9c6a8a5f6e0da77ddfdcd5d079fb2d.

El usuario ejecutará lectura y aplicación desde WSL con sus credenciales SQL ocultas en el VPS. Pruebas SQL temporales, limpieza y respaldo siguen siendo condiciones anteriores al DDL. No afirmar instalación hasta recibir updated/completed,17 migraciones y verificaciones correctas. Después, Seguridad permite asignar Repartidor - Camioneta (sistema) al chofer; los permisos del rol anterior siguen siendo aditivos y deben revisarse expresamente si se desea limitar sus accesos.

## Cierre local

Pasaron las 70 pruebas Python focalizadas, incluidas 17 regresiones nuevas. Cubren transformación exclusiva del Name, las cuatro colisiones reservadas, rechazo de inventarios/handlers no esperados, preflight real con transporte simulado en historias16/17, allowlist mediante query_sql real con proceso SQL simulado, gate administrativo anterior al runner, helper antiguo/nuevo con SHA estricto, callback aislado por UUID y preservación del rol legacy.

El fingerprint se compara antes, tras rollback y tras aplicar; la repetición comprueba historia, filas y revisión, apoyada en el SQL fijo guardado por MigrationId. No se ejecutó SQL real localmente en esta revisión. La fixture se ejecutará obligatoriamente en el VPS antes de stage/stop; el reporte anterior sólo acredita el SQL v3 sin esa fixture.

Helper antiguo aceptado: 6ddf2a1759a23b40bebe19ebfd5f793e28b0587a69035faec891458bd536431e. Helper adaptado/fuente: 11e632669a295fff86d9a86905d57e1046e511022405cd9dcdf2c9323184c87a. La adaptación de firma y ancla produce exactamente los bytes del segundo; cambios desconocidos se rechazan.

Artefactos verificados:

- PRODIMT_Instalar_I22_v4.py: 52,328 bytes; SHA256 9b65851d9eea95b8af4f83d01650e37d446be528a813f450cd52c5a7a3e438fe.
- PRODIMT_Reparto_I22.zip: 20,559,085 bytes; SHA256 91312936b2614641f30016b21e0ce8b29c85a0e264c2c9b0d285402156633fa2.
- Payload sin cambios: fb0485190d68285cc121603a3b946a19fb9c6a8a5f6e0da77ddfdcd5d079fb2d, 543 archivos, hashes y cuatro DLL de backend/validation idénticas.

CRC exterior/interior, SHA256SUMS, manifiesto y cargador exportado correctos. El script interior del ZIP coincide con el instalador independiente. La prueba del cargador local sólo adapta en memoria la lectura por UID1000; no debilita la guarda de propietario enviada al VPS. No hubo preflight remoto, SQL operativo o activación desde este agente. Se conserva el estado remoto confirmado: historia16, aplicación anterior sana, despliegue I22 pendiente.
