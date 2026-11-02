# I14 — Validación del mensaje de acceso simplificado

Fecha operativa: 1 de noviembre de 2026. Rama `codex/i14-reminder-message-template`, basada en I13 `7c950822795d9a367043958618f4ed2d53668e2b`.

## Cambio y límites

Un único cambio de plantilla en `access-token-manager.ts`: retirar las dos leyendas de entrega/corte y anteponer «Haz tu pedido aquí:» a la URL completa. Se preserva nombre, descripción, fragmento del token, vencimiento y formato horario. Todos los canales existentes reutilizan este método. Tokens nuevos y existentes siguen con las mismas reglas de servidor; la fecha se conserva en el portal.

El instalador de frontend conserva el patrón validado de I13: preflight de lectura, hash del paquete/archivos, proceso y backend esperados, salud y autorización HTTP, snapshot privado, cambio atómico del enlace, verificación de assets y rollback del frontend. Exige frontend I13 y backend I12 para no retroceder el catálogo. Sin SQL directo, migraciones, secretos, reinicios, revocaciones ni mensajes enviados.

## Comprobaciones

- `npm ci` con ambos lockfiles: correcto; sin cambios de dependencias.
- `npm run build`: correcto; 493.06 kB iniciales. Advertencias de estilos heredadas de I13, sin ampliar presupuestos.
- Pruebas del instalador/paquete I14: 15 correctas, incluidos rechazo de I11/I12, lectura sin escrituras, activación/repetición, rollback por fallo de asset/interrupción y backend alterado.
- Pruebas E2E actualizadas y regresión completa: pendientes en este checkpoint.
- La descarga del Chromium del lockfile falló localmente por un archivo de navegador inválido. No se cambió el lockfile ni se afirmó ejecución de navegador por ese intento.
- `graphify update .`: herramienta no instalada; no existe grafo versionado que actualizar.

## Estado

Compilación local verificada. Paquete final y validación remota pendientes de cierre. No se accedió al VPS o a la base real, no se reescribieron tokens ni se enviaron mensajes.
