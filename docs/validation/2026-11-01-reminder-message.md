# I14 — Validación del mensaje de acceso simplificado

Fecha operativa: 1 de noviembre de 2026. Rama `codex/i14-reminder-message-template`, basada en I13 `7c950822795d9a367043958618f4ed2d53668e2b`.

## Cambio y límites

Un único cambio de plantilla en `access-token-manager.ts`: retirar las dos leyendas de entrega/corte y anteponer «Haz tu pedido aquí:» a la URL completa. Se preserva nombre, descripción, fragmento del token, vencimiento y formato horario. Todos los canales existentes reutilizan este método. Tokens nuevos y existentes siguen con las mismas reglas de servidor; la fecha se conserva en el portal.

El instalador de frontend conserva el patrón validado de I13: preflight de lectura, hash del paquete/archivos, proceso y backend esperados, salud y autorización HTTP, snapshot privado, cambio atómico del enlace, verificación de assets y rollback del frontend. Exige frontend I13 y backend I12 para no retroceder el catálogo. Sin SQL directo, migraciones, secretos, reinicios, revocaciones ni mensajes enviados.

## Comprobaciones

- `npm ci` con ambos lockfiles: correcto; sin cambios de dependencias.
- `npm run build`: correcto; 493.06 kB iniciales. Advertencias de estilos heredadas de I13, sin ampliar presupuestos.
- Pruebas del instalador/paquete I14: 15 correctas, incluidos rechazo de I11/I12, lectura sin escrituras, activación/repetición, rollback por fallo de asset/interrupción y backend alterado.
- [CI 41383717018](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41383717018), fuente `768230826a3eff9d02cea0620acd814cb0047359`: cuatro trabajos correctos, 335 Domain/Application (5 + 330), 66 SQL/criptografía, 134 E2E y 320 Python más smoke Production.
- E2E de enlace nuevo y Guardar y abrir WhatsApp comprueban nueva llamada a la acción, URL y vencimiento, con ambas leyendas ausentes. La regresión nueva prueba mismo contenido en vista previa, copia, WhatsApp y fallback manual incluso al cambiar de fecha de entrega, sin crear/editar otro token; el enlace conserva el secreto en el fragmento.
- La descarga del Chromium del lockfile falló localmente por un archivo de navegador inválido. No se cambió el lockfile ni se afirmó ejecución de navegador por ese intento.
- `graphify update .`: herramienta no instalada; no existe grafo versionado que actualizar.

## Estado

Implementación publicada en [PR #6](https://github.com/romanrfhack/pedidosProdimt/pull/6), rama `codex/i14-reminder-message-template`, sobre I13. Fuente de aplicación/paquete `768230826a3eff9d02cea0620acd814cb0047359`. Las actualizaciones documentales posteriores no cambian esos binarios o su pin.

Entrega verificada y guardada: `PRODIMT_Plantilla_Mensaje_I14.zip`, 198059 bytes, SHA256 `c8b51437fa71e0fe61164cafabee7902e46a8aa69d4b630c5a55e67db957e39b`. Payload SHA256 `34c1b9d6cc6c6df93e3af8759fbbb30480a76960dc132f292dae9789296537db`. Once archivos frontend/licencias. CRC de ambos ZIP, SHA256SUMS, manifiesto, pines del instalador exportado, dependencias relativas de JS/HTML y ausencia de leyendas retiradas comprobados. Se ejecutó `load_payload` del instalador final contra el payload final, sin preflight o cambios remotos.

Siguiente: verificar hashes, copiar instalador/payload, preflight `planned/completed` sin cambios y aplicación `updated/completed`, `frontend_verified=true`, `health_verified=true`; luego Ctrl+F5 y revisar mensaje/portal/catálogo. No se accedió al VPS o a la base real, no se reescribieron tokens ni se enviaron mensajes. Instalación y aceptación de negocio pendientes.

## Instalación confirmada — 2 de noviembre de 2026

El usuario proporciona SHA256 del ZIP correcto y salida de `20261101-reminder-message-install1`:

- Lectura: `planned/completed`, `changes_made=false`, `base_release_verified=true`, `health_verified=true`. `frontend_verified=false` corresponde a esa etapa anterior a la activación.
- Aplicación: `updated/completed`, `changes_made=true`, `base_release_verified=true`, `frontend_verified=true`, `health_verified=true`.
- `database_migrations_run`, `database_queries_run`, `backend_changed`, `service_restarted`, `password_changed` y `secret_values_printed` en false.
- `source_commit`: `768230826a3eff9d02cea0620acd814cb0047359`.
- `new_frontend`: `/var/www/prodimt-pedidos/frontend/releases/reminder-message-768230826a3e`.
- `private_snapshot`: `/var/backups/prodimt-pedidos/reminder-message-20261102T142214Z-0a20c3900c52`.

Esto sustituye el pendiente de instalación anterior y confirma la activación/verificación por reporte del usuario. No se ejecutó SSH independiente desde este entorno ni se recibió todavía aceptación de vista previa/copia/WhatsApp. Siguiente: Ctrl+F5, revisar un acceso vigente y comprobar saludo/descripción, llamada a la acción, URL, vencimiento y ausencia de las dos leyendas; la fecha sigue dentro del portal. No repetir la instalación o regenerar tokens por este resultado. Sólo documentación modificada en esta sesión; sin nuevas pruebas de aplicación, consultas a la base, cambios de paquete o envío de mensajes.
