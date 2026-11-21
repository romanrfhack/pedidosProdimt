# Responsable de Máquina

La vista `/admin/maquina` agrupa por cliente los pedidos de la máquina seleccionada y conserva sus partidas. Muestra kilos físicos, kilos vinculados, pendientes de producir y kilos sin cliente de la jornada (Ciudad de México). Capturar para un pedido guarda lote y asignación juntos. Asignar un lote existente cambia la demanda pendiente y conserva el total físico.

## Operación

1. En **Seguridad**, asignar el rol de sistema **Responsable de Máquina** a la cuenta operativa.
2. Con una cuenta administrativa, abrir **Mi máquina**, seleccionar la máquina y usar **Responsables** para vincular la cuenta. Cualquier acceso se valida también en la API por máquina.
3. Dar de alta al personal desde **Configurar equipo** con una cuenta autorizada para administrar personal. Cada trabajador es un registro de producción independiente de las cuentas de acceso.
4. En **Equipo fijo** asignar Maquinista, Recolector 1, Recolector 2 y Empaquetador, con fecha de vigencia. **Equipo de hoy** permite ausencias, suplencias, traslados y retiro del ajuste. Una vacante queda visible y permite continuar la captura. Para mover personal se requiere acceso a ambas máquinas y un destino sin ocupante. Un ajuste diario conserva hora, actor, historia y, cuando aplica, las dos plazas del traslado.
5. **+ kg** registra un incremento por partida. **Listo** libera las partidas completas del pedido en la máquina. El detalle permite liberar kilos parciales. Las liberaciones quedan visibles en la actividad del día.
6. **Lotes sin cliente** permite producir, asignar al pedido compatible o registrar entrega a Embarques. El movimiento de custodia conserva lote, fecha, máquina y actor. Una vez entregados, los kilos continúan en el total físico y salen de la disponibilidad de la máquina. Para su gestión posterior se requiere la futura vista de Embarques.

## Persistencia

La migración aditiva `20261117145405_AddMachineWorkspace` agrega seis tablas de alcance, avisos, custodia y equipo, más el saldo liberado en las asignaciones. Los lotes existentes conservan sus registros inmutables. Las operaciones usan la transacción existente de recibos durables, con llaves idempotentes y control de versión de pedidos/equipo. Las asignaciones liberadas quedan protegidas ante reversión directa.

La migración registra el rol de sistema, sus derechos acotados y los derechos de gestión del Administrador. Si existe un rol personalizado con el mismo nombre, conserva sus cuentas y permisos. El rol nuevo se distingue por su identificador de sistema. Las máquinas quedan disponibles para administración y requieren asignación explícita para las cuentas operativas.

El módulo registra liberaciones y custodia de máquina a Embarques. El traspaso a la camioneta y el conteo del chofer se conservan para el incremento de Embarques.

## Validación y activación

Validar la migración 20 → 21 en una base SQL Server de prueba con el perfil de permisos de la aplicación. El caso HTTP focal está en `ProductionManagementSqlTests.MachineWorkspace_EndToEnd_RequiresRoleAndMachineAndPreservesCustody`. Verificar con cuentas separadas el acceso, la captura duplicada, los saldos y un traslado temporal. Comprobar también que una cuenta con una sola máquina recibe 403 para otras.

La operación en el VPS requiere respaldo, verificación del usuario SQL efectivo y despliegue coordinado de API y frontend. La validación local no aplica migraciones a la base operativa.
