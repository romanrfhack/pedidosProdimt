# Estado vigente de PRODIMT

Corte documental: 3 de noviembre de 2026, Ciudad de México. Un estado de instalación se fundamenta en reportes del destino; no se deduce de compilar o de encontrar código.

## Versiones conocidas

| Concepto | Evidencia / resultado |
|---|---|
| Fuentes auditadas | `aa802e210d15c70784f79dea2580709166b843c7` (I23 revisión 3, integra RF1), bundle verificado |
| Backend instalado confirmado | `delivery-0cfe3ef511d8` (I22) |
| Frontend instalado confirmado | `delivery-flow-426c360e1365` (RF1), fuente `426c360e13656ac8c39442b2b492c15393e979b8` |
| Historia SQL conocida | 17 migraciones en último control reportado; esta auditoría no consulta SQL productivo |
| I23 | Código preparado, migración 18 propuesta; último intento reportado detenido en SQL aislado antes de cambios |
| Comprobación pública | Índice coincide con RF1; HTTP 200 y `/health/db` devuelve `reachable` |
| Aceptación funcional | No consta validación completa de usuarios, ruta real, vueltas y entregas; sigue pendiente |

I23 incluye horarios, martes inhábil y solicitudes restringidas de enlaces identificables vencidos. Esas reglas no deben describirse como ya instaladas en el backend I22. El último error conocido es `validation_expected_rollback_not_observed`; no hay evidencia que autorice quitar la guarda ni afirmar la causa del SQL.

## Módulos y alcance

| Módulo | Código disponible | Instalación conocida | Pendiente |
|---|---|---|---|
| Pedidos/corrección/catálogos/precios/tokens | Implementado e integrado | Incorporado en I22 | Consolidación y aceptación de flujo |
| Seguridad I18 | Implementada, permisos aditivos en servidor | Incorporada por integración I19/I22 | Límite de login; auditoría/hardening |
| Producción I19 | Lotes/asignaciones físicas implementados | Incorporada en I22 | Recuperación de envío incierto y medición |
| Reparto I22 | Rutas/vueltas, reservas, parciales, custodia, bitácora | I22 confirmado; RF1 mejora acceso/preparación | Configuración/aceptación reales y hallazgos de lectura/contexto |
| Reparto RF1 | Guía, acceso visible, panel y guardas | Confirmado por `updated/completed` | Validar accesos efectivos y recorrido del chofer |
| Horarios/solicitudes I23 | Implementado en aa802 | No confirmado; gate aislado bloqueado | Diagnóstico y validación SQL/HTTP específica |
| Cobros/facturas/foto/firma/envases/offline | No implementado como módulo operativo | No aplica | Futuro alcance, excluido de consolidación |

## Calidad y trabajo actual

[Auditoría](../audits/2026-11-03/AUDITORIA.md): 24 hallazgos, 22 acciones pendientes y dos contradicciones documentales resueltas. Tres P1 abiertos: reintento de producción, límite de login y bloqueo de validación I23. No se confirmó P0 ni un compromiso de seguridad.

Las comprobaciones ejecutadas y sus límites están únicamente en [VALIDACION](../audits/2026-11-03/VALIDACION.md). Mantener separados Domain/Application, SQL Server, navegador con API simulada, recorrido real y aceptación humana. No sustituir evidencia de una fase por otra.

La siguiente prioridad es el [plan de consolidación](PLAN-CONSOLIDACION.md), no añadir funciones. Se preservan contratos, transacciones, recibos, unidades e historia; no se recomienda reescritura.

## Antecedentes

- RF1: [registro de instalación y validación](../validation/2026-11-03-delivery-flow.md).
- I22: [instalación confirmada](../validation/2026-11-03-driver-van-installed.md).
- I23: [evidencia de preparación previa](../validation/2026-11-03-schedules-on-delivery.md); el estado actual de este documento prevalece sobre pendientes antiguos.
- [Estado histórico completo](historico/ESTADO-hasta-aa802.md), [bitácora](BITACORA.md), [matriz funcional](MATRIZ-FUNCIONAL.md).
