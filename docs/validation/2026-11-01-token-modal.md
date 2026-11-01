# I10 — Modal responsivo de tokens

## Alcance y revisión

Sólo presentación Angular y distribución nativa del frontend sobre I09 instalado. Modal nativo con bloqueo de fondo, foco restaurado y cierre coherente durante operaciones; dos columnas PC, una en tableta/móvil, cuerpo desplazable y cierre visible. Estado del borrador y enlace conservado en padre; apertura/cierre no llama API de creación ni reinicia la configuración. Revocación informa en el modal. Revisión independiente estática de UI y empaquetado sin bloqueantes.

## Validación local

- npm run build correcto. Presupuestos conservados; advertencia previa de admin-customer-configuration.css (~7.55 kB) bajo el máximo de 8 kB.
- python3 -m unittest discover -s tests/pilot -q: 250 correctas, 12 nuevas. Integridad, target exacto, staging, idempotencia, rollback/fallo/interrupción y backend invariable verificados con fixtures aislados.
- graphify update . no disponible (127); no existe grafo previo en esta copia.
- Pruebas de navegador/CI y ZIP final pendientes en este registro inicial.

## Despliegue

I09 sigue siendo el último despliegue confirmado por el usuario. No se ha accedido por SSH desde este entorno ni instalado I10. El actualizador sólo cambia el enlace del frontend, conserva backend/SQL/configuración y no reinicia el servicio. Aceptación real pendiente después de aplicar el paquete.
