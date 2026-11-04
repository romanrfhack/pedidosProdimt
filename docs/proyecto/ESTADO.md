# Estado vigente de PRODIMT

Corte documental: 3 de noviembre de 2026, Ciudad de México. Un estado de instalación se fundamenta en reportes del destino; no se deduce de compilar o de encontrar código.

## Versiones conocidas

| Concepto | Evidencia / resultado |
|---|---|
| Fuentes auditadas | `aa802e210d15c70784f79dea2580709166b843c7` (I23 revisión 3, integra RF1), bundle verificado |
| Backend instalado confirmado | `ordering-schedules-i23-aa802e210d15` (I23) |
| Frontend instalado confirmado | `ordering-schedules-i23-aa802e210d15` (I23 integrado con RF1) |
| Historia SQL conocida | 18 migraciones en el reporte I23; sin consulta SQL productiva desde este agente |
| I23 | Instalado: `updated/completed`, SQL exacto, rollback/replay, permisos y limpieza verificados según reporte del usuario |
| Comprobación pública | El índice RF1 fue comprobado antes del cierre I23; no representa el release actual |
| Aceptación funcional | No consta validación completa de usuarios, ruta real, vueltas y entregas; sigue pendiente |

I23 está confirmado por [reporte del VPS](../validation/2026-11-03-schedules-installed.md), recibido después del corte de auditoría. El corrector `8621e964` y el cierre `d8e1db45` se integran sin cambiar la aplicación instalada aa802. El bloqueo F03 queda resuelto en ese entorno; permanece pendiente automatizar la cobertura CI específica F13. Aceptación de navegador pendiente.

## Módulos y alcance

| Módulo | Código disponible | Instalación conocida | Pendiente |
|---|---|---|---|
| Pedidos/corrección/catálogos/precios/tokens | Implementado e integrado | Incorporado en I22 | Consolidación y aceptación de flujo |
| Seguridad I18 | Implementada, permisos aditivos en servidor | Incorporada por integración I19/I22 | Límite de login; auditoría/hardening |
| Producción I19 | Lotes/asignaciones físicas implementados | Incorporada en I22 | Recuperación de envío incierto y medición |
| Reparto I22 | Rutas/vueltas, reservas, parciales, custodia, bitácora | I22 confirmado; RF1 mejora acceso/preparación | Configuración/aceptación reales y hallazgos de lectura/contexto |
| Reparto RF1 | Guía, acceso visible, panel y guardas | Confirmado por `updated/completed` | Validar accesos efectivos y recorrido del chofer |
| Horarios/solicitudes I23 | Implementado en aa802 | I23 confirmado | Validación SQL/HTTP funcional específica y aceptación |
| Cobros/facturas/foto/firma/envases/offline | No implementado como módulo operativo | No aplica | Futuro alcance, excluido de consolidación |

## Calidad y trabajo actual

[Auditoría](../audits/2026-11-03/AUDITORIA.md): 24 hallazgos, 22 acciones pendientes y dos contradicciones documentales resueltas. El bloqueo P1 F03 se cierra con evidencia posterior; las correcciones de producción y login se abordan en C1. No se confirmó P0 ni un compromiso de seguridad.

Las comprobaciones ejecutadas y sus límites están únicamente en [VALIDACION](../audits/2026-11-03/VALIDACION.md). Mantener separados Domain/Application, SQL Server, navegador con API simulada, recorrido real y aceptación humana. No sustituir evidencia de una fase por otra.

La siguiente prioridad es el [plan de consolidación](PLAN-CONSOLIDACION.md), no añadir funciones. Se preservan contratos, transacciones, recibos, unidades e historia; no se recomienda reescritura.

## Antecedentes

- RF1: [registro de instalación y validación](../validation/2026-11-03-delivery-flow.md).
- I22: [instalación confirmada](../validation/2026-11-03-driver-van-installed.md).
- I23: [evidencia de preparación previa](../validation/2026-11-03-schedules-on-delivery.md); el estado actual de este documento prevalece sobre pendientes antiguos.
- [Estado histórico completo](historico/ESTADO-hasta-aa802.md), [bitácora](BITACORA.md), [matriz funcional](MATRIZ-FUNCIONAL.md).
