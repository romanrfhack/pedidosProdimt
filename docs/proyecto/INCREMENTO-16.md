# Propuesta I16 — Pedir otro producto y completar su configuración

Fecha: 2 de noviembre de 2026, America/Mexico_City.
Estado: análisis y propuesta funcional; no implementado, sin migración, paquete ni instalación. Se espera una definición operativa sobre el tratamiento de las partidas nuevas antes de configurar su precio.

## Solicitud y alcance

En Captura manual del cliente y en captura administrativa, permitir seleccionar un producto del catálogo compartido que aún no está configurado para ese cliente y registrar su cantidad. Crear una bandeja para Administración General, que complete precio y los demás datos habituales de la relación cliente-producto. El cliente no necesita conocer máquina, precio interno, posición de interpretación o abreviatura. No crear productos globales desde la captura.

Se propone el botón «Pedir otro producto» y modal «Agregar producto al pedido»: buscador/selector, nombre, unidad informativa del catálogo y cantidad. Por ejemplo, un producto en paquetes solicita cantidad de paquetes; no convertir a kilos. Sólo catálogo activo, sin equivalencias archivadas y con unidad confirmada. El cliente no elige otra unidad ni ve descripciones internas, precios ajenos, asignaciones o datos de otros clientes. Una restricción/inactivación explícita de la relación cliente-producto debe respetarse; no habilitarla por el simple hecho de aparecer en el catálogo global.

Agregar al modal modifica sólo el borrador. No crear pendientes, relaciones permanentes ni pedidos hasta Confirmar pedido/Guardar pedido. Evitar repetir un producto en el borrador; editar su cantidad en la misma línea. Cancelar no persiste nada. Borradores con producto extra no pueden volver a captura rápida perdiendo esa línea en silencio; conservarla o exigir una resolución explícita antes de cambiar modo.

## Guardado propuesto

El servidor obtiene nombre/unidad/estado desde catálogo y determina si falta la configuración; no confía en esos atributos enviados por el cliente. Conserva pertenencia al CustomerId autenticado, versión del perfil, validación de fecha de entrega/corte, unidades vigentes, cantidades y control de pedido existente. Si hay un pedido de esa entrega, conservar el flujo actual de corrección, sin crear otro automáticamente.

Guardar pedido, referencias de nuevos productos, pendiente y auditoría en la misma transacción. Reintentos usan la misma operación durable: no duplican pedido ni pendiente. Un pendiente abierto por cliente-producto puede reunir referencias a varias partidas/pedidos; sus cantidades y fechas siguen perteneciendo a cada pedido, sin sumarlas como un pedido nuevo.

No agregar posiciones ni aprobar perfiles de captura rápida automáticamente. Registrar el requerimiento separado del perfil aprobado hasta su revisión para no alterar el significado de cantidades como 13+7 ni deshabilitar la captura habitual sin revisión explícita.

## Bandeja administrativa

Nombre propuesto: «Productos por configurar», con contador visible. Mostrar cliente, producto, unidad/cantidad solicitada, fecha de entrega, pedido(s), origen cliente/administración, fecha/autor del registro, datos faltantes y estado. Distinguir esta bandeja de pedidos tardíos/adicionales, clientes sin pedido y avisos de edición de demanda.

Abrir la configuración existente del cliente, enfocada al producto solicitado, con acceso a precio e historial, cantidad sugerida, orden visual, posición del mensaje, abreviatura, estado activo y preferencia de máquina si corresponde. Validar versiones para no sobrescribir cambios concurrentes ni aprobar el perfil por completar sólo el precio. Registrar quién revisó/resolvió, cuándo y qué cambió; completar el pendiente sólo después de comprobar en servidor los datos requeridos.

La audiencia funcional pedida es Administrador General. Hoy la aplicación sólo distingue AdminAccess y CustomerAccess; no existe un rol granular de Administrador General. La implementación debe definir su política compatible con la cuenta administradora del piloto, sin dar al cliente acceso al catálogo administrativo, precios o bandeja. No implementar toda la matriz futura de roles por este cambio.

## Precio y trazabilidad

Un precio faltante permanece null/pendiente; no se convierte a cero. Un cero requiere definición explícita. Reutilizar revisiones por cliente-producto con vigencia por fecha de servicio, usuario, motivo y control de concurrencia. Para fijar precio, el servicio existente exige guardar antes la relación cliente-producto; el flujo de revisión debe respetarlo y no cerrar el pendiente si una etapa falla.

I11 fija precio en cada partida al confirmar. Crear/cambiar después la tarifa del cliente no completa automáticamente la partida ya guardada sin precio. Es necesario un caso de uso explícito de resolución inicial del precio de partidas pendientes, limitado a los pedidos/producto/cliente revisados, con revisión de vigencia y vista previa de las partidas afectadas. Auditar valor anterior null, importe aplicado, revisión utilizada, actor, fecha y motivo. Conservar precios ya fijados y el historial anexable; no recalcular pedidos históricos como efecto secundario ni anticipar cargos/saldos/cobros.

## Producción — hechos comunicados, implementación diferida

- Burreras y tortillas de harina se compran y revenden: no requieren máquina de producción propia.
- Hay productos fabricados internamente con máquina preferida y otros cuya asignación se decide cada día según urgencia, velocidad, personal y otros factores.
- El responsable de producción conservará la decisión y registrará qué asignó a cada máquina; la asignación diaria no debe confundirse con la preferencia permanente del cliente-producto.
- No obligar a completar una máquina para cerrar una configuración exclusivamente comercial. No inferir modo de abastecimiento sólo por nombre, ausencia de máquina o pertenencia al perfil.
- Modelo futuro a definir: procedencia explícita (reventa/producción/por confirmar), asignación diaria, cantidades/estado y auditoría. No introducir ahora planificación, optimización, reparto de lotes o nueva captura de producción.
- Hoy OrderLineFactory hereda la máquina predeterminada del cliente si no hay preferencia del producto. Antes de admitir nuevos artículos debe evitarse atribuir esa máquina automáticamente a productos de reventa o cuya asignación esté pendiente. No inventar asignaciones para poder guardar.

## Decisión operativa pendiente

¿Una partida con un producto nuevo entra al flujo operativo normal mientras el precio está pendiente, o debe esperar revisión del Administrador General? Recomendación para validar: registrar el pedido y continuar con estado comercial «Precio pendiente», conservando el aviso hasta resolverlo, para no detener la captura por una tarifa faltante. Esta recomendación no está aceptada por el usuario todavía y no debe implementarse como autorización automática de producción/entrega. Si se decide retener sólo la partida nueva, los productos habituales del mismo pedido deberían poder continuar; eso exige distinguir el estado de cada partida de la revisión del pedido completo.

## Revisión técnica realizada

Base de diseño: rama codex/i15-responsive-customers, checkpoint afb8f3ad9c33a8960d3874c14d462bb8dd0c646d. I15 estaba en validación; antes de implementar integrar su cierre para conservar I14/mensaje e I15/rejilla de clientes. No afirmar I15 instalado por su existencia en código.

- CustomerOrderCaptureService.ResolveManual y AdminOrderCaptureService: sólo aceptan productos activos del perfil actual.
- CustomerOrderCaptureDtos y customer-capture-api.service: el catálogo visible deriva de productos del cliente; se necesita consulta segura de productos adicionales, no reutilizar una respuesta de administración.
- OrderLineFactory: crea partida con unidad confirmada, precio snapshot por fecha y máquina preferida/predeterminada; permite precio y máquina null.
- CustomerProductPricingService.ChangeAsync: requiere relación configurada, escribe revisión/auditoría y no actualiza partidas de pedidos.
- OrderLine: snapshot de precio, revisión/vigencia y máquina nullable. Precio faltante produce importe null.
- Program.cs: políticas actuales por tipo de actor Admin/Customer, no rol granular de Administrador General.

No se consultó la BD ni el VPS. Captura aportada se revisó en privado y no se incorpora al repositorio.

## Fases y validación previstas

1. Cerrar la decisión operativa anterior y concretar contratos/migración aditivos de pendiente/referencias/resolución, preservando operaciones existentes.
2. Implementar catálogo público mínimo, modal reutilizable y guardado transaccional en ambas capturas, con bandeja administrativa y edición enfocada.
3. Completar precio/relación y resolución auditada de partidas aún sin precio, con concurrencia, reintentos y conservación de snapshots anteriores.
4. Probar autorización entre clientes/admin, catálogo/relaciones inactivos, unidades cambiantes, duplicados, cancelación/borrador/cambio de modo, cruce de las18h, operación repetida/respuesta perdida, rollback, precio vigente/sin precio, pendientes concurrentes, revisión y vistas responsivas. Ejecutar SQL Server real para las transacciones y permisos nuevos; migración con respaldo/verificación antes del despliegue.

La nota de producción permanece diferida y no se presenta como código o funcionalidad entregada. No hay cambios de aplicación ni pruebas de ejecución nuevas en esta sesión de diseño.
