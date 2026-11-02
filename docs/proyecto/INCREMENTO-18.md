# I18 — Usuarios, roles y permisos

## Estado de instalación actualizado

I18 quedó instalado mediante el paquete integrado I19, confirmado por el reporte del usuario: `updated/completed`, migraciones13→16 y backend/frontend `production-d3a11420b202`, salud y frontend verificados. La ejecución posterior del instalador I18v2 se rechazó correctamente por `previous_release_not_supported` sin cambios. No reinstalar el payload I18 ni permitir que sustituya I19. La siguiente acción es validar la pantalla `/admin/seguridad`; la aceptación de negocio sigue pendiente. Ver el estado vigente en CONTINUAR y ESTADO.


Autorizado el 2 de noviembre de 2026. El usuario solicita CRUD de usuarios, roles y permisos; asignación de varios roles por usuario y permisos individuales adicionales. La autorización amplía la administración básica histórica de Fase 1. Se desarrolla sobre I17 `cdbfec3071bc2046f0398fbdad6b62bdca6de748`, preservando I12–I15/I17 y sin incorporar la rama paralela I16.

## Regla de acceso

```text
permisos efectivos = unión de permisos activos de roles activos + permisos directos activos
```

Un permiso recibido por varios orígenes aparece una sola vez. Retirar un rol elimina únicamente ese origen; otro rol o una concesión directa puede conservarlo. Retirar una concesión directa no deniega lo heredado. No se introducen excepciones Deny. Una concesión directa basta para autorizar la acción correspondiente, sin exigir además pertenecer a un rol concreto.

Las cuentas y permisos se resuelven en servidor en cada petición administrativa. El JWT conserva autenticación, actor administrativo y validación de sesión I05; no es fuente de permisos vigentes. Desactivar la cuenta o retirar permisos se aplica a la siguiente petición. El cliente externo sigue aislado por CustomerId/token y no puede administrar seguridad.

## Administración

- Usuarios: listado, búsqueda, alta, edición de nombre/estado/contraseña, selección multirrol, permisos directos y baja lógica que conserva historial. Nunca devolver PasswordHash o contraseñas en consultas/auditoría.
- Roles: CRUD y selección agrupada de permisos; las relaciones son explícitas y no se copian al usuario.
- Permisos: CRUD de metadatos y estado; clave estable utilizada por el código, nombre visible, módulo y descripción. Crear una clave personalizada no inventa una acción API: sólo las claves consumidas por la aplicación habilitan funciones existentes.
- Seguridad administrativa requiere `security.manage`, reservado a operadores de confianza. El rol de sistema Administrador permanece activo, con permisos del sistema completos y protegido frente a eliminación/redefinición destructiva. La migración asigna este rol a todas las cuentas administrativas preexistentes para conservar su acceso. No existe concesión automática de administrador para usuarios nuevos.
- Se rechazan autobloqueo, eliminación del último administrador y eliminación de roles/permisos del sistema o aún asignados. Las mutaciones exigen revisión vigente; un 409 conserva el borrador y exige revisar la versión actual antes de reintentar.
- Cambios y asignaciones se auditan atómicamente, sin secretos. La protección del último administrador y el control de revisión deben mantenerse frente a operaciones simultáneas.

## Interfaz

Ruta prevista `/admin/seguridad`, pestañas Usuarios, Roles y Permisos, búsqueda/estado y editor amplio. En PC se aprovecha el ancho disponible; en móvil los controles y el detalle se apilan sin desplazamiento horizontal de página. La asignación muestra orígenes Heredado de: rol(es) y Directo, junto al resultado efectivo, para evitar interpretar quitar el permiso directo como una denegación.

La navegación y las rutas reflejan permisos efectivos cargados desde el servidor; la API sigue siendo la autoridad. Los errores 401/403, carga fallida y revisión obsoleta no habilitan acciones silenciosamente. Se conservan pedidos, captura, perfiles, precios, acceso de clientes y diseño I17.

## Actualización nativa

Preparación de paquete con backend/frontend y migración aditiva 13→14. Bases admitidas: backend I12 `access-tokens-65560ca3cf98` y frontend I15 `customer-catalog-afb8f3ad9c33` o I17 `pending-customers-cdbfec3071bc`. Cualquier otro release, incluido un I16 instalado, debe rechazarse antes de escribir para evitar sobreescritura de trabajo paralelo.

Antes de DDL: inspección de configuración/esquema/permisos, snapshot privado y respaldo SQL COPY_ONLY/CHECKSUM con RESTORE VERIFYONLY correcto. Preservar api.env, configuración efectiva, clave JWT y clave dedicada de accesos I12; no revocar tokens, rotar claves, restablecer contraseñas ni ejecutar bootstrap. Reiniciar únicamente PRODIMT al activar. La recuperación automática a I12 sólo se permite inmediatamente si la revisión y el grafo de seguridad conservan el estado inicial de migración. I12 otorga administración total a toda cuenta interna activa: después de altas/restricciones I18, volver a ese binario sería una ampliación de privilegios. Si la seguridad cambió o no puede comprobarse, detener la API, conservar los enlaces actuales y reportar `recovery_manual_required`; requiere recuperación manual hacia un binario compatible con I18. Nunca restaurar SQL automáticamente ni forzar un rollback de binarios anterior sin esta comprobación.

El SQL exacto entregado debe probarse mediante sqlcmd con sus separaciones de lote/transacción, además de EF MigrateAsync, por el antecedente SQL207 de I12. Paquete fijado por commit/SHA256 y hashes por archivo, sin configuración productiva, datos reales, mapas o símbolos.

## Criterios de aceptación y estado

Implementación, validación SQL Server/CI y paquete final completados. Application351+Domain5 correctas, Angular build correcto,12 nuevas E2E y38 regresiones correctas,16 pruebas específicas del paquete correctas; EF sin diferencias pendientes de modelo. Ver `../validation/2026-11-02-identity-administration.md`. CI completo:356 Domain/Application,72 SQL/criptografía,163 E2E y366 Python, más smoke Production. Fuente y paquete verificados en la evidencia vinculada. Este documento no acredita despliegue, migración remota o aceptación de negocio.

1. Anónimo obtiene 401; cliente externo y administrativo sin permiso obtienen 403 en recursos protegidos.
2. Un rol, varios roles y concesión directa habilitan la misma acción; retirar uno de dos orígenes conserva el permiso y retirar el último lo revoca en la siguiente petición.
3. CRUD/asignaciones persisten, rechazan claves/IDs desconocidos y mantienen auditoría atómica sin secretos.
4. Se protegen Administrador, último administrador y propia sesión frente a pérdida de gestión; concurrencia obsoleta produce 409.
5. Migración 13→14 conserva cuentas, contraseñas, pedidos, catálogos, perfiles, precios, tokens y configuración; repetir no duplica ni vuelve a conceder roles retirados.
6. Verificar búsqueda/editor/procedencia/errores y ausencia de desbordamiento a 390/768/1440/1920px; conservar captura y accesos existentes.
7. Entrega instalable sólo tras comprobar binarios, SQL por transporte real, pines/checksums y flujo de recuperación. Instalación/aceptación remotas se registrarán por separado.
