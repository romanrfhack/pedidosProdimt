# I12 — Copia, edición y reenvío de accesos activos

Autorizado el 1 de noviembre de 2026. El usuario no necesita conservar los accesos anteriores: autoriza revocarlos todos y crear nuevos a demanda. No se ejecuta una revocación remota durante desarrollo; se aplicará al instalar la actualización nativa.

## Interfaz y reglas

El modal I10 conserva responsividad, foco, bloqueo de fondo y borrador al cerrar/reabrir dentro de la misma configuración. Muestra sólo accesos habilitados, sin vencer y de cliente activo, calculado con hora del servidor. Se conserva la auditoría y los registros anteriores, aunque no aparezcan en esta pantalla. El contador corresponde al listado utilizable.

Cada fila permite Copiar token, Copiar enlace, Editar, Abrir WhatsApp y Revocar. Editar precarga el formulario de creación con descripción/vigencia y teléfono del cliente. Guardar cambios mantiene token, identidad y enlace; Guardar y abrir WhatsApp recupera el valor actual con autorización y prepara mensaje con fecha/vigencia actualizadas. El operador confirma el envío en WhatsApp. No se programa o envía masivamente.

Un formulario con versión obsoleta conserva el borrador y requiere recargar metadata antes de guardar. Tokens vencidos, revocados o de cliente inactivo no se revelan ni editan; no se reactivan desde edición. Acortar vigencia se aplica a las sesiones abiertas. Ampliar vigencia no amplía un JWT ya emitido: reabrir el enlace permite nueva sesión. Hora de entrega/corte, captura rápida/manual, pedidos, perfiles y precios I11 no cambian.

## Seguridad y contratos

Hash existente continúa siendo la credencial de búsqueda y autenticación. Copia cifrada adicional con AES-GCM de .NET, nonce aleatorio, autenticación ligada a cliente/ID y clave aleatoria dedicada de 32 bytes, persistente en configuración privada fuera de releases y de la base. No reutilizar la clave JWT, generar valores alternativos silenciosos o guardar tokens en texto plano. Falta/corrupción de clave bloquea creación/recuperación con error fijo, sin rotar o revocar accesos nuevos automáticamente.

GET administrativo mantiene contrato anterior y añade filtro activeOnly y metadata version/isUsable/canReveal/reminderLink. El listado no descarga secretos. POST por token /reveal autoriza cliente/ID, comprueba vigencia y devuelve sólo ese secreto con Cache-Control no-store. Auditar recuperación sin valor, enlace, teléfono o payload secreto. PATCH por token requiere expectedVersion, description y expiresAt; concurrencia transaccional, guardas de estado y auditoría. Crear/revocar siguen autorizados únicamente para administración.

## Migración y despliegue

I11 nativo confirmado como base exacta prices-148a52dd7428 (doce migraciones). Migración 20261101190000_AddRecoverableCustomerAccessTokens añade ProtectedToken nullable, MetadataVersion e IsReminderLink y revoca/audita tokens anteriores atómicamente. No borra su historial ni afecta pedidos. Sesiones de esos tokens pierden acceso por validación de IsActive en cada solicitud. Repetir instalación no revoca tokens protegidos nuevos; el reintento tras rollback I11 contempla cualquier nuevo token heredado sin cifrado con la misma guarda/auditoría idempotente.

Actualizador con preflight de lectura, copia privada de configuración, clave estable y respaldo SQL COPY_ONLY/CHECKSUM/VERIFYONLY antes de migrar. Reinicia sólo PRODIMT al activar backend/frontend. Conserva clave añadida aun si revierte aplicación; nunca restaura automáticamente SQL ni regenera una clave existente por un error. Paquete sin claves, configuración productiva, datos reales, mapas o símbolos.

## Validación y estado

Código `65560ca3cf98f3a1cc44e7f3ef50d4276e1f3df8`, [PR #4](https://github.com/romanrfhack/pedidosProdimt/pull/4), rama codex/i12-access-token-management sobre I11. [CI final 41350276236](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41350276236) con cuatro trabajos correctos: 335 Application/Domain, 66 del proyecto SQL Server/criptografía, 121 E2E y 288 Python más smoke Production. Capturas PC/tableta/móvil e integridad del ZIP revisadas. Paquete PRODIMT_Gestion_Tokens_I12.zip, 7,304,892 bytes, SHA256 `ee52910b3d1d0be45241a4d7accb47c86f31ce6b45bbafc3b7c7369a97b676df`. Instalación y revocación remotas aún pendientes; I11 sigue siendo el último despliegue confirmado. Al aplicar I12 se revocan los accesos anteriores autorizados y se crean nuevos a demanda. No repetir I09/I10/I11 ni reparaciones/importaciones para esta actualización.

La revocación, auditoría, concurrencia entre hosts y esquema/cleanup exactos del instalador se ejecutaron en SQL Server real. También se comprobaron fallo de auditoría con rollback completo, preservación de accesos protegidos en reintento, recuperación con la misma clave y ausencia de secretos en auditoría. Las pruebas de navegador cubren 320/390/768/1366/1920, copia tras recargar, edición conservando token/enlace/segundos de vencimiento, conflicto versionado, envío preparado y fallback de clipboard. Revisión independiente sin bloqueantes.

El ZIP incluye instalador fijado por SHA y payload de 68 archivos, SQL incremental, avisos de terceros y backend dependiente de .NET10 para el Linux existente. AssemblyInformationalVersion coincide con el commit publicado. No incluye claves, configuración privada, mapas o PDB. SHA256SUMS y load_payload/make_installer del script exportado con el payload exportado correctos, sin ejecutar preflight o modificaciones remotas. [Evidencia](../validation/2026-11-01-access-token-management.md).

## Incidente de instalación: compilación del SQL idempotente

El primer --apply que superó preflight falló con SQL207 tras respaldo y persistencia de clave; rollback recuperó aplicación anterior y conservó clave. No inferir commit13 de database_migrations_run. Instalador revisión2 conserva payload/binarios65560ca y separa el batch antes de referenciar columnas recién agregadas, manteniendo una transacción por la misma sesión sqlcmd. Revalidar primero sólo lectura; no borrar clave, restaurar SQL automáticamente o aplicar DDL manual. Ver evidencia del incidente y regresión real de sqlcmd en CI.
