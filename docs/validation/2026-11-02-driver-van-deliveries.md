# I22 — Implementación y validación de Mi reparto

Fecha operativa: 2 de noviembre de 2026, America/Mexico_City.

## Base y autorización

El usuario aprobó el diseño de Mi reparto y autorizó su implementación. Se recuperó el bundle I21 `496eb7094c05e3269f941ca6f87af6a5a88e4c98`, incluidos I16/I18/I19; se integraron las correcciones posteriores del instalador I18 desde `4cd4644a505384383ba55f68fba4383b36eac650`. La base de trabajo quedó en `7096442`. No se partió de main desactualizado ni se reemplazó la producción I19.

El antecedente de rechazo automático de publicación de I19 permanece documentado. No se intentó publicar esta unión mediante otro transporte ni se afirma PR/CI remoto. Fuente y compilación se entregan por bundle/parche y paquete nativo de validación.

## Implementado

- Ruta propia `/reparto` y gestión `/admin/reparto`, navegación e inicio conforme permisos efectivos I18.
- Rol Repartidor - Camioneta: `delivery.read.own` y `delivery.operate.own`; `delivery.manage` para gestión, concedido al Administrador del sistema.
- Rutas con código existente, chofer habitual, recorridos por fecha/vuelta y sustitución explícita con motivo.
- Reservas por partida/unidad, conteo, carga, entregas incrementales, devoluciones, transferencia de custodia, liberación y correcciones anexadas a bitácora.
- Cantidad comercial original conservada, pendiente global y custodia diferenciados. Los ejemplos50/25/20 y10+2 usan únicamente datos ficticios.
- Pedido completo calculado con todas sus partidas y vueltas, incluidas partidas todavía sin asignar. El chofer consulta todas las partidas de sus pedidos asignados, sin recibir otros clientes ni listas globales de usuarios/rutas.
- Maps HTTPS validado, dirección e instrucciones versionadas. La actualización explícita de una ficha abierta conserva condiciones especiales del pedido y registra evento; no modifica fichas históricas cerradas.
- Propiedad comprobada antes de recuperar recibos, permisos efectivos por petición, control de versión e idempotencia durable. Errores conservan borrador; respuesta incierta conserva referencia y contenido en la sesión del navegador.
- Guardas de corrección de pedido para impedir borrar identidades comprometidas o reducir por debajo del reparto reservado/cargado/entregado. Correcciones compatibles conservan identidades y requieren revisión de cambios antes de continuar.

## Persistencia

Migración aditiva `20261103020000_AddDeliveryOperations`:16→17 migraciones, cinco tablas (DeliveryRoutes, CustomerDeliveryLocations, DeliveryTrips, DeliveryAssignments y DeliveryEvents), restricciones de cantidades/custodia/compromiso, índices y claves foráneas restringidas. No se agregan clientes, ubicaciones reales, cuentas de chofer o asignaciones operativas por instalación.

Los movimientos conservan historia inmutable. El instalador añade permisos de lectura/inserción para eventos y deniega actualización/borrado. El esquema esperado se deriva del SQL exacto de EF; la comparación de CHECK usa un AST acotado que conserva agrupaciones aritméticas y booleanas.

## Comprobaciones ejecutadas

| Comprobación | Resultado |
|---|---|
| Base integrada antes de modificar |401 pruebas .NET correctas:5 Domain+396 Application |
| Solución final Release |436 pruebas .NET correctas:5 Domain+431 Application;0 omitidas |
| Casos nuevos de reparto SQLite |34 correctos; cantidades, custodia, reservas, rollback, reintentos durables, corrección comercial y privacidad |
| Rol del repartidor mediante HTTP real/TestServer |Permisos propios;200 en reparto propio y403 en gestión/pedidos globales/seguridad |
| EF `has-pending-model-changes` |Sin diferencias pendientes |
| SQL Server project |Compila,0 errores;3 advertencias previas de tests históricos SQL interpolado |
| SQL incremental EF |Generado desde I19 hasta I22, guardas/parser correctos; no ejecutado contra SQL Server |
| Angular producción |Build correcto; módulo lazy; sin nuevas dependencias ni ampliación de presupuestos CSS |
| Google Maps |5 casos válidos y8 rechazados comprobados sobre función fuente |
| Playwright reparto |11 pruebas compiladas/coleccionadas; ejecución pendiente |
| Paquete I22 focalizado |19 pruebas Python correctas |
| Python completo |436 pruebas:432 correctas,1 omitida y3 fallos preexistentes I19 por UID1000, idénticos a la base |
| Revisión independiente |Corregidos hallazgos de reservas liberadas, pedido compuesto, Maps después de asignar, propiedad antes de recibo, nombres200 y horario específico del pedido; sin P1 conocidos al cerrar |

Los tres fallos Python históricos son `test_build_and_exported_installer_agree_on_pins_hashes_and_all_three_migrations`, `test_readonly_recovery_accepts_stopped_service_and_apply_activates_only_i19` y `test_recovery_marker_pins_source_payload_and_private_environment`, en test_production_package. Fueron reproducidos sin I22. No se relajaron las comprobaciones de propietario del instalador para hacerlos pasar.

## Límites de validación

No hay instancia SQL Server aislada ni Docker disponible en este entorno. Se prepararon tres pruebas SQL/HTTP en `DeliveryManagementSqlTests` para reservas simultáneas entre hosts, autorización/recibo durable y revocación de gestión. Compilar ese proyecto, probar SQLite o analizar SQL no equivale a ejecutarlas sobre SQL Server.

Chromium local no inicia por una restricción de sockets del entorno. El navegador remoto tampoco puede acceder al servidor local del ejecutor. No se afirma ejecución Playwright, revisión visual de la UI implementada o verificación de tamaños reales en navegador. Las once pruebas quedan listas para un entorno compatible.

El paquete se rotula **COMPILACIÓN DE VALIDACIÓN**. Antes de aplicar en producción faltan: suite SQL Server aislada y migración mediante sqlcmd real, pruebas de rollback/recuperación del transporte exacto y ejecución Playwright. Ninguna prueba usa datos operativos, credenciales productivas o el VPS del usuario.

## Comandos reproducibles

```bash
dotnet test src/Prodimt.Pedidos.sln -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet tool restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api -c Release --no-build
dotnet build tests/Prodimt.Pedidos.SqlServer.Tests/Prodimt.Pedidos.SqlServer.Tests.csproj -c Release -m:1 -nr:false -p:UseSharedCompilation=false
python3 -m unittest discover -s tests/pilot -p test_delivery_package.py -v
```

```bash
# En una instancia aislada de SQL Server, con PRODIMT_SQL_TEST_CONNECTION configurada privadamente:
dotnet test tests/Prodimt.Pedidos.SqlServer.Tests/Prodimt.Pedidos.SqlServer.Tests.csproj -c Release --filter FullyQualifiedName~DeliveryManagementSqlTests
# Después ejecutar las regresiones SQL y el transporte sqlcmd del SQL empaquetado.
```

```bash
cd apps/prodimt-pedidos-web
npm ci
npm run build
cd ../../tests/e2e
npm ci
npx playwright install --with-deps chromium
npx playwright test specs/delivery-operations.spec.ts
```

## Configuración operativa posterior a validación e instalación

1. Seguridad: crear o seleccionar cuentas y asignar Repartidor - Camioneta. No se generan contraseñas ni usuarios automáticamente.
2. Reparto: registrar códigos de ruta coincidentes con los clientes existentes y definir chofer habitual.
3. Completar ubicación e instrucciones con información confirmada. No copiar las fichas incompletas del antiguo manual como hechos.
4. Crear una vuelta para fecha/ruta, escoger sustituto si corresponde y reservar cantidades por partida.
5. El repartidor inicia sesión, consulta Mi reparto y confirma conteo/carga/entrega real con observaciones.
6. El responsable revisa pendientes, custodia, devoluciones, transferencias y cierre. Las notas de impago o envases no generan movimientos financieros ni de inventario.

## Estado de entrega

Implementación y verificaciones locales anteriores completas. El paquete y el bundle se fijan al commit de aplicación y se verifican al construirlos. Publicación GitHub, instalación, migración remota y aceptación de negocio permanecen separadas y pendientes. No se operó el VPS, se enviaron mensajes o se modificaron datos reales.
