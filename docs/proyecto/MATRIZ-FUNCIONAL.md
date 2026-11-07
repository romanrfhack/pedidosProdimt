# Matriz funcional y estado de evidencia

C2, 7 de noviembre: F13 **43/43 HTTP/SQL Server** y F14 **3/3 navegador/API/SQL**
correctos localmente sobre `5e7d8e836f0951aefc613e0363d740b26ec59153`,
limpieza verificada. [Evidencia](../validation/2026-11-07-c2-wsl.md).
CI remoto y aceptación humana pendientes; sin instalación C2 ni push.

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


Base auditada: `aa802e210d15c70784f79dea2580709166b843c7`. Instalación/aceptación: [ESTADO](ESTADO.md). Referencias exactas por hallazgo: [AUDITORÍA](../audits/2026-11-03/AUDITORIA.md).

| Requisito / área | Código existente | Prueba / tipo de evidencia | Estado | Pendiente / límite |
|---|---|---|---|---|
| Pedidos y correcciones | CustomerOrderCaptureService; AdminDailyOrderService; CustomerToday/AdminToday | CustomerOrderCaptureTests; DailyOrderServiceTests; specs de pedidos | Implementado; integrado e instalado | Aceptación real y F04/F07/F09 |
| Productos/precios/configuración | AdminCatalogs; snapshots en líneas; CustomerProductSetupService | Pruebas de catálogo/configuración/precio; SQL de configuración | I16 incorporado en I19/I22 | Precio null no equivale a cero; F06 |
| Tokens/WhatsApp individual | AdminCustomerAccessTokenService; protector AES-GCM; AccessTokenManager | AccessTokenManagementTests; SQL/criptografía; UI tokens | Implementado e incorporado | Abrir WhatsApp no acredita envío |
| RBAC y cuentas | SecurityService; AdminPermissionMiddleware; SecurityDefaults | SecurityAdministrationTests y suite SQL | I18 incorporado en I19/I22 | F02 y F24; asignación real no inspeccionada |
| Producción física | ProductionManagementService; AdminOperations | ProductionManagementTests y ProductionManagementSqlTests; UI mock | I19 incorporado en I22 | F01/F12/F16; no se fabrica al aceptar |
| Rutas/vueltas/reservas | DeliveryManagementService; DeliveryEndpoints; DeliveryWorkspace | DeliveryManagementServiceTests; SQL entre hosts; UI mock | I22 instalado | Configurar ruta/vuelta/partidas y validar usuario real |
| Parciales/custodia/historia | Ledger y eventos Delivery; transferencia/devolución/cierre | Unit/SQL de reparto; escenario 50/25/20 | Implementado; validación SQL I22 informada | No reducir pedido ni liberar custodia por entregar menos |
| Acceso/preparación RF1 | App routes; AdminToday; DeliveryOnboarding/Workspace | delivery-integration.spec; guardas; tests RF1 | RF1 actualizado y assets verificados | Aceptación visual/funcional aún no acreditada |
| Horarios y solicitudes I23 | OrderSchedulingService; DeliveryDatePolicy; CustomerLinkAccessPolicy | OrderingScheduleTests en SQLite; browser mock; faltan HTTP SQL propios | I23 aa802 instalado por reporte del VPS | F05/F08/F13; F03 resuelto, migración 18 aplicada; C1 no desplegado |
| Inducción/Maps/instrucciones | Campos de ficha cliente y snapshots por asignación | Pruebas de snapshots y URL Maps permitidas | Soporte implementado; fichas reales incompletas | No importar ni inventar ubicaciones históricas; F10/F11 |
| Cobro/foto/firma/envases/offline | Modalidad de pago y notas, sin módulos operativos equivalentes | No hay cobertura que acredite módulos de estas operaciones | Fuera del alcance actual | Notas de impago/caja no son ledger financiero ni inventario |

Los nombres de pruebas identifican cobertura disponible, no aseguran que se haya ejecutado en esta auditoría. [Resultados por entorno](../audits/2026-11-03/VALIDACION.md). Ningún conteo de build/tests sustituye aceptación de negocio.

Seguimiento posterior a la auditoría: [correcciones C1](../corrections/C1.md). Las pruebas de la tabla son antecedentes; el registro C1 distingue correcciones locales de la versión instalada.
