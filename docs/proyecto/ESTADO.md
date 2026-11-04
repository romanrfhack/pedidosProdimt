# Estado vigente de PRODIMT

Corte documental: 4 de noviembre de 2026, Ciudad de México. Un estado de instalación se fundamenta en reportes del destino; no se deduce de compilar o de encontrar código.

## Versiones conocidas

| Concepto | Evidencia / resultado |
|---|---|
| Fuentes auditadas | `aa802e210d15c70784f79dea2580709166b843c7` (I23 revisión 3, integra RF1), bundle verificado |
| Backend instalado confirmado | `ordering-schedules-i23-aa802e210d15` (I23) |
| Frontend instalado confirmado | `ordering-schedules-i23-aa802e210d15` (I23 integrado con RF1) |
| Historia SQL conocida | 18 migraciones en el reporte I23; sin consulta SQL productiva desde este agente |
| C1 | Candidato nativo integrado preparado para WSL; nueva ejecución de 499 pruebas .NET, 42 componentes, 101 SQL Server y 577 Python correctos (cuatro omisiones históricas); build/typecheck/modelo EF, validador portátil 7/7 y gate SQL exacto 18→19 correctos. Navegador WSL y despliegue pendientes; VPS repetirá sus controles SQL |
| I23 | Instalado: `updated/completed`, SQL exacto, rollback/replay, permisos y limpieza verificados según reporte del usuario |
| Comprobación pública | El 4 de noviembre `/health/db` devuelve `status: ok`, `database: reachable`. Acredita salud actual, no identifica release ni confirma C1 |
| Aceptación funcional | No consta validación completa de usuarios, ruta real, vueltas y entregas; sigue pendiente |

I23 está confirmado por [reporte del VPS](../validation/2026-11-03-schedules-installed.md), recibido después del corte de auditoría. El corrector `8621e964` y el cierre `d8e1db45` se integran sin cambiar la aplicación instalada aa802. El bloqueo F03 queda resuelto en ese entorno; permanece pendiente automatizar la cobertura CI específica F13. Aceptación de navegador pendiente.

**C1 no está desplegado.** La [preparación nativa del 4 de noviembre](../validation/2026-11-04-c1-native.md) registra el candidato sobre I23, los gates obligatorios y sus límites. Chromium local aborta antes de assertions; el wrapper WSL exige los 240 casos del mismo commit con API simulada, sin omisiones, antes de aplicar. El primer pase SQL completo encontró siete fallos históricos de fixtures (94/101 correctas); las cuatro regresiones nuevas F06 pasaron. Se corrigieron las fixtures, sin relajar la aplicación, y la repetición completa dio **101/101 correctas, cero fallos y omisiones**, en SQL Server 2022 Developer `16.0.4205.1`. Se conserva también el pase inicial. La validación SQL temporal no acredita instalación en el VPS.

El gate SQL exacto también pasó en SQL Server real: 18→19, metadata estricta,
preservación de 33 tablas, Unicode/permisos de aplicación, rollback/replay,
downgrade con pérdida rechazado y limpieza verificada. El instalador debe
repetirlo en el destino antes de detener PRODIMT; no reemplaza el reporte de
instalación ni la validación de navegador.

El validador portátil también pasó **7/7 casos EF/HTTP con SQL Server real**
y verificó la limpieza de sus dos bases UUID. Es una validación aislada del
candidato; no una operación sobre la base productiva ni una aceptación humana.

## Módulos y alcance

| Módulo | Código disponible | Instalación conocida | Pendiente |
|---|---|---|---|
| Pedidos/corrección/catálogos/precios/tokens | Implementado e integrado | Incorporado en I22 | Consolidación y aceptación de flujo |
| Seguridad I18 | Implementada, permisos aditivos en servidor; límite de login en candidato C1 | Incorporada por integración I19/I22; C1 pendiente | Desplegar/verificar límite de login; auditoría/hardening |
| Producción I19 | Lotes/asignaciones físicas; recuperación de envío incierto en candidato C1 | Incorporada en I22; C1 pendiente | Desplegar/aceptar recuperación de reintentos y medición |
| Reparto I22 | Rutas/vueltas, reservas, parciales, custodia, bitácora | I22 confirmado; RF1 mejora acceso/preparación | Configuración/aceptación reales y hallazgos de lectura/contexto |
| Reparto RF1 | Guía, acceso visible, panel y guardas | Confirmado por `updated/completed` | Validar accesos efectivos y recorrido del chofer |
| Horarios/solicitudes I23 | Implementado en aa802 | I23 confirmado | Validación SQL/HTTP funcional específica y aceptación |
| Cobros/facturas/foto/firma/envases/offline | No implementado como módulo operativo | No aplica | Futuro alcance, excluido de consolidación |

## Calidad y trabajo actual

[Auditoría original](../audits/2026-11-03/AUDITORIA.md): 24 hallazgos. [C1](../corrections/C1.md) corrige en código F01/F02/F04/F05/F07/F09, implementa F06 con cuatro regresiones HTTP/SQL correctas y gate de entrega independiente, atiende parcialmente F08/F14 y cierra F03 por evidencia posterior. F19/F20 ya estaban resueltos documentalmente. Los cambios C1 no están desplegados; quedan 12 hallazgos sin cambio y dos parciales, además de sus validaciones de aceptación. No se confirmó P0 ni un compromiso de seguridad.

Las comprobaciones originales están en [VALIDACION](../audits/2026-11-03/VALIDACION.md); la ejecución posterior y los límites de cada corrección están en [C1](../corrections/C1.md). Mantener separados Domain/Application, SQL Server, navegador con API simulada, recorrido real y aceptación humana. No sustituir evidencia de una fase por otra.

La siguiente prioridad es el [plan de consolidación](PLAN-CONSOLIDACION.md), no añadir funciones. Se preservan contratos, transacciones, recibos, unidades e historia; no se recomienda reescritura.

## Antecedentes

- RF1: [registro de instalación y validación](../validation/2026-11-03-delivery-flow.md).
- I22: [instalación confirmada](../validation/2026-11-03-driver-van-installed.md).
- I23: [evidencia de preparación previa](../validation/2026-11-03-schedules-on-delivery.md); el estado actual de este documento prevalece sobre pendientes antiguos.
- [Estado histórico completo](historico/ESTADO-hasta-aa802.md), [bitácora](BITACORA.md), [matriz funcional](MATRIZ-FUNCIONAL.md).
