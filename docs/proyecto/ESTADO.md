# Estado vigente de PRODIMT

## Responsable de Máquina

Implementación local sobre `759c68f`. Vista compacta de producción con totales, captura, avisos de liberación y lotes sin cliente. Equipo fijo por vigencia y ajustes diarios con alcance por máquina. Migración aditiva 20 → 21. [Operación, permisos y activación](RESPONSABLE-DE-MAQUINA.md).

## Mi reparto por etapas

En el código de `/reparto`, el chofer puede filtrar los clientes de su vuelta por conteo, carga y entrega. La etapa activa muestra el nombre completo y las demás muestran RC, CC o RE. Cada etapa indica cuántos clientes tienen partidas pendientes. Las filas plegadas muestran cantidades por unidad. El botón rápido abre el diálogo de una partida o despliega las partidas para elegir. «Todas las paradas» permite consultar el detalle de clientes que ya salieron de las listas por etapa.

El ajuste usa las operaciones de reparto existentes. La última versión instalada que confirmó el usuario es `delivery-auto-89ed72778f30`, con las 20 migraciones aplicadas y la API activa. La vista por etapas está pendiente de publicación y aceptación en el teléfono del repartidor.

## Ticket diario en Pedidos

El candidato agrega «Ver ticket» a pedidos registrados o aceptados de clientes. Reúne las partidas con cantidad positiva del mismo cliente y fecha de servicio, utiliza el precio y el importe aplicados en el pedido y muestra el consumo en MXN. La vista reutiliza los permisos de consulta o captura de pedidos y permite imprimir en rollos de 58 y 80 mm desde el diálogo del navegador.

Mientras se concilia la fuente de saldos, el adeudo anterior puede capturarse para la impresión actual. El total sólo aparece con adeudo explícito y precios completos. El candidato no genera movimientos de cuenta. Quedan pendientes el cruce de tarifas y saldos del Excel con el catálogo operativo, la prueba en la térmica de Mostrador y las notas semanales.

## Vuelta inicial automática y suplencia diaria

El release operativo `capture-routes-1b49aaeefebd` confirmó salud y evidencia de instalación el 7 de noviembre. La rama local sobre `0820087` prepara Vuelta 1 al registrar o aceptar un pedido con ruta activa y chofer habilitado para la fecha de servicio operativa. Las paradas se muestran por preparar. Confirmar el conteo genera la reserva y libera las diferencias para otra vuelta. La salida detiene nuevas incorporaciones y el saldo continúa en Pendientes de asignar.

La suplencia se guarda por ruta y fecha. Una Vuelta 1 que aún admite cambio de chofer conserva su identidad y deja el cambio en bitácora. Mostrador puede elegir próxima entrega automática desde las 18:00 de CDMX para la fecha calculada por el servidor.

La migración aditiva `20261108143413_AddDeliveryTripDepartureAndDayDrivers` agrega la hora de salida y la tabla de suplencias. Para la publicación nativa se necesita aplicar 19→20 y verificar los permisos efectivos `1110` (`SELECT`, `INSERT`, `UPDATE` y `DENY DELETE`) sobre `dbo.DeliveryRouteDayDrivers` para `prodimt_pedidos_app`. La validación de SQL Server y la instalación de este incremento son pasos de la siguiente entrega.

El candidato `7d09e565` pasó C1 en WSL. Tras corregir la comparación de rutas en `actor_isolation`, `df79c83` pasó los 43 casos HTTP con SQL Server y dos de los tres escenarios con navegador y API real. La limpieza se verificó. La prueba del reparto parcial ahora espera que termine la consulta antes de abrir el conteo. Quedan pendientes C2 completo, C1 y la suite SQL para el nuevo commit.

C2, 7 de noviembre: **gate local completo correcto**, F13 **43/43 HTTP/SQL Server**
y F14 **3/3 navegador/API/SQL**, limpieza verificada, `recoveryPending=false`.
Fuente probada `5e7d8e836f0951aefc613e0363d740b26ec59153`.
[Causa, corrección y reportes](../validation/2026-11-07-c2-wsl.md).
CI remoto y aceptación humana pendientes; C1 R5 sigue instalado. Sin push.

## Estado posterior confirmado: C1 R5 instalado; C2 en trabajo

La evidencia posterior del usuario y su log WSL confirman **C1 R5 instalado**:
fuente `fdb81ea2f92011a74f2089642b5d3a9e46b26351`, backend/frontend
`consolidation-c1-fdb81ea2f920`, historia SQL **19**, última
`20261104041928_ExpandAuditMetadata`, AuditLogs **1111**,
`updated/completed`, `mode=apply`, salud/frontend/esquema y preservación
verificados, `recovery_pending=false`. [Procedencia y siete casos reales](../validation/2026-11-04-c1-r5-installed.md).
La aceptación humana completa continúa pendiente. C2 trabaja aisladamente
F13 (HTTP/SQL Horarios) y F14 (recorrido real/comandos); no hay instalación C2.
No hacer push. Los apartados anteriores a esta evidencia son historia de su fecha.


Los apartados siguientes conservan el corte anterior; la evidencia R5 del encabezado prevalece.

Corte documental: 4 de noviembre de 2026, Ciudad de México. Un estado de instalación se fundamenta en reportes del destino; no se deduce de compilar o de encontrar código.

## Versiones registradas antes de recibir el resultado R5

| Concepto | Evidencia / resultado |
|---|---|
| Fuentes auditadas | `aa802e210d15c70784f79dea2580709166b843c7` (I23 revisión 3, integra RF1), bundle verificado |
| Backend instalado confirmado | `ordering-schedules-i23-aa802e210d15` (I23) |
| Frontend instalado confirmado | `ordering-schedules-i23-aa802e210d15` (I23 integrado con RF1) |
| Historia SQL conocida | 18 migraciones en el reporte I23; sin consulta SQL productiva desde este agente |
| C1 | R4 pasó 240/240, pero falló el preflight VPS sin cambios: esperaba AuditLogs1100 y la identidad real I23 tiene1111. R5 corrige entrega y recuperación para preservar1111; 74/74 controles C1 y 607 Python (604 correctos, tres omisiones root, cero fallos). Gates navegador de la nueva fuente y SQL corregido/destino pendientes. [Evidencia R5](../validation/2026-11-04-c1-r5-permissions.md). C1 sin desplegar |
| I23 | Instalado: `updated/completed`, SQL exacto, rollback/replay, permisos y limpieza verificados según reporte del usuario |
| Comprobación pública | El 4 de noviembre `/health/db` devuelve `status: ok`, `database: reachable`. Acredita salud actual, no identifica release ni confirma C1 |
| Aceptación funcional | No consta validación completa de usuarios, ruta real, vueltas y entregas; sigue pendiente |

I23 está confirmado por [reporte del VPS](../validation/2026-11-03-schedules-installed.md), recibido después del corte de auditoría. El corrector `8621e964` y el cierre `d8e1db45` se integran sin cambiar la aplicación instalada aa802. El bloqueo F03 queda resuelto en ese entorno; permanece pendiente automatizar la cobertura CI específica F13. Aceptación de navegador pendiente.

**C1 no está desplegado.** La [preparación nativa del 4 de noviembre](../validation/2026-11-04-c1-native.md) registra el candidato sobre I23, los gates obligatorios y sus límites. Los primeros intentos locales de Chromium abortaban antes de assertions; la ejecución WSL posterior sí ejecutó los 240 casos. El wrapper exige 240 correctas del mismo commit con API simulada, sin omisiones, antes de aplicar. El primer pase SQL completo encontró siete fallos históricos de fixtures (94/101 correctas); las cuatro regresiones nuevas F06 pasaron. Se corrigieron las fixtures, sin relajar la aplicación, y la repetición completa dio **101/101 correctas, cero fallos y omisiones**, en SQL Server 2022 Developer `16.0.4205.1`. Se conserva también el pase inicial. La validación SQL temporal no acredita instalación en el VPS.

El gate SQL exacto también pasó en SQL Server real: 18→19, metadata estricta,
preservación de 33 tablas, Unicode/permisos de aplicación, rollback/replay,
downgrade con pérdida rechazado y limpieza verificada. El instalador debe
repetirlo en el destino antes de detener PRODIMT; no reemplaza el reporte de
instalación ni la validación de navegador.

El validador portátil también pasó **7/7 casos EF/HTTP con SQL Server real**
y verificó la limpieza de sus dos bases UUID. Es una validación aislada del
candidato; no una operación sobre la base productiva ni una aceptación humana.

## Historial de ejecución WSL hasta la preparación R4

El usuario confirmó Chromium operativo: **79 correctas, una fallida y 160 no ejecutadas** en el primer pase. La prueba esperaba un texto anterior al vigente desde I23; la [segunda revisión](../validation/2026-11-04-c1-browser-wsl.md) corrigió sólo ese selector y permitió recoger todos los fallos.

El pase completo de R2 `e9ef18b73e534602df41a1df0fc221586a715e74` dio **231 correctas, nueve fallidas, cero omitidas y cero inestables**. La [revisión R3](../validation/2026-11-04-c1-browser-r3.md) corrige el error auxiliar que ocultaba pendientes tras una revisión, cinco selectores exactos de reparto y dos espaciados de escritorio de configuración. Se comprobaron **47/47 componentes, 16/16 casos focales de navegador y tipado E2E correcto**; los nueve fallos originales están cubiertos por esos casos focales. Python: **585 casos, 581 correctos y cuatro omisiones históricas**, cero fallos. El script canónico, incluido build, y la ejecución completa del commit final siguen pendientes; sus resultados se adjuntarán al paquete como `BROWSER_C1_VALIDACION.json` y `.md`. Las 240 correctas y la repetición WSL siguen siendo obligatorias; C1 no está desplegado.

La repetición canónica de R3 ejecutó todos los casos y obtuvo **239 correctos,
uno fallido, cero omitidos y cero inestables**. El documento de configuración
midió 770 px en 1366×768 frente al máximo conservado de 769; no hubo SCP/SSH.
La [revisión R4](../validation/2026-11-04-c1-browser-r4.md) reduce sólo el
padding vertical de `.config-page` desde 1200 px. Los cuatro viewports focales
pasan y 1366×768 queda en 768 px, con controles completos. El nuevo commit,
paquete y gate completo siguen pendientes; C1 no está desplegado.

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
