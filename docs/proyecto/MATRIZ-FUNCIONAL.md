# Matriz funcional y estado de evidencia

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
| Horarios y solicitudes I23 | OrderSchedulingService; DeliveryDatePolicy; CustomerLinkAccessPolicy | OrderingScheduleTests en SQLite; browser mock; faltan HTTP SQL propios | Implementado en aa802; no instalado confirmado | F03/F05/F08/F13; migración 18 pendiente |
| Inducción/Maps/instrucciones | Campos de ficha cliente y snapshots por asignación | Pruebas de snapshots y URL Maps permitidas | Soporte implementado; fichas reales incompletas | No importar ni inventar ubicaciones históricas; F10/F11 |
| Cobro/foto/firma/envases/offline | Modalidad de pago y notas, sin módulos operativos equivalentes | No hay cobertura que acredite módulos de estas operaciones | Fuera del alcance actual | Notas de impago/caja no son ledger financiero ni inventario |

Los nombres de pruebas identifican cobertura disponible, no aseguran que se haya ejecutado en esta auditoría. [Resultados por entorno](../audits/2026-11-03/VALIDACION.md). Ningún conteo de build/tests sustituye aceptación de negocio.
