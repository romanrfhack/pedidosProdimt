# Plan integral vigente

Fecha de referencia: 24 de octubre de 2026.

## Objetivo

Trazar el trabajo desde la recepción del pedido hasta producción, preparación, reparto, cobro e inventario, con implantación gradual. El primer beneficio es que administración capture una vez y cada área consulte los productos y cantidades correctos.

Se reutiliza `romanrfhack/pedidosProdimt` (.NET 10, Angular 21 y SQL Server). `romanrfhack/prodimt` queda como referencia de interfaz. Al comenzar se consideró una instalación nueva; el inventario posterior confirmó PRODIMT nativo en el VPS con SQL, releases y configuración existentes. Se conserva esa instalación y el historial de desarrollo, sin crear un tercer repositorio ni reemplazar los datos actuales.

## Etapas y criterios de salida

| Etapa | Resultado | Criterio de salida |
|---|---|---|
| 0. Preparación | Base técnica comprobada, catálogos, unidades, perfiles y reglas del piloto | Pruebas técnicas y revisión de datos; pendientes visibles y sin equivalencias inventadas |
| 1. Pedido y demanda | Captura única, importación diaria revisable, correcciones auditables, demanda por máquina y preparación por ruta | Una semana de comparación del piloto; cada cantidad tiene cliente, producto, unidad, fecha y origen; pendientes excluidos de demanda autorizada |
| 2. Operación física | Producción real, empaque, etiquetas, carga, viajes y entrega parcial/devolución | Diferenciar pedido, producido, preparado, cargado y entregado; evidencias y diferencias registradas |
| 3. Cobranza | Precios por cliente, cargos, abonos, aplicación de pagos, tickets, saldos y liquidación del repartidor | Conciliar venta, saldo anterior, cobro solicitado y pago recibido sin equipararlos |
| 4. Inventario y costos | Materias primas, lotes, recetas, consumo, merma, compras y costos | Trazabilidad de lotes y conciliación física/contable del inventario |
| 5. Automatización | Integración oficial de WhatsApp y mejoras de capacidad/rutas | Compatibilidad de cuentas/canales verificada, interpretación controlada e idempotencia |

La interpretación de texto pegado de WhatsApp puede incorporarse entre etapas 1 y 2. No requiere esperar a una integración oficial. Sólo propone capturas; la ambigüedad conserva revisión humana.

## Piloto

El usuario seleccionó una ruta mediante una fotografía: 22 renglones impresos y un cliente manuscrito adicional, 23 candidatos en total. El detalle identificable vive en el paquete privado. La numeración impresa salta un número y repite otro: no es identificador estable.

Incluir todas las máquinas o centros de preparación necesarios para estos clientes. Un cliente puede pedir varios productos y éstos pueden corresponder a máquinas distintas. La asignación por cliente no basta. El primer incremento no divide una misma línea entre dos máquinas; esa capacidad pertenece a planificación posterior.

Los marcadores de llave y ticket se capturan por separado. La ausencia de un icono es desconocida hasta validación; no implica pago semanal ni inexistencia de llave. Las notas de colocación de mercancía requieren instrucciones reales del negocio.

Antes del piloto operativo se validan identidad de clientes, productos habituales, unidades, orden de interpretación, asignaciones y calendario. Las cifras manuscritas de la fotografía no se importan como pedidos actuales. La hora impresa `10:30 AM` no se transforma en hora límite ni ventana de entrega sin confirmación.

Avance de revisión I02: recibida hoja revisada del usuario, con identidad/ruta y condiciones explícitas de acceso, además de dos notas internas. Los campos todavía vacíos permanecen separados del catálogo listo para cargar. La validación de código y SQL Server no activa por sí sola el ensayo de la ruta.

## Incrementos

| ID | Entrega | Relación con etapas |
|---|---|---|
| I01 | Recuperación técnica, unidades explícitas, configuración del piloto, datos internos de entrega/cobro, preferencia de máquina por producto y consulta de demanda | Etapa 0 y base de etapa 1 |
| I02 | Validación/carga privada del piloto, importación diaria con vista previa y origen, identidad de operación, versiones/correcciones y manejo de concurrencia | Completar captura fiable de etapa 1 |
| I03 | A: paquete de instalación y datos pendientes; B: instalación y carga revisada; C: ensayo de una semana y evaluación de diferencias. Listas/etiquetas y registro físico cuando se habiliten | Salida de etapa 1 e inicio de etapa 2 |
| I04 | Avisos persistentes de cambios de demanda, reconocimiento por máquina y configuración enfocada/versionada del cliente | Refuerzo operativo para el ensayo; preparación de etapa 2 |

I05 incorpora cambio de contraseña propia e invalidación de sesiones, ya instalado. I06, autorizado el 28 de octubre, mejora catálogo y captura de cliente con modal y estado diario/descanso semanal, como preparación del piloto de etapas 0 y 1.

No asignar fechas de terminación sin confirmar disponibilidad operativa, despliegue y calidad de los datos. Cada incremento debe tener una aceptación técnica y una aceptación de negocio separadas.

## Siguiente paso tras la carga de catálogos

El 28 de octubre el usuario confirma carga sin error. Se recomienda comenzar la etapa 1 con captura administrativa existente de 3–5 pedidos reales y comprobar cliente, producto, unidad, fecha de entrega, autorización y demanda. Conservar WhatsApp/llamada como recepción y registrar el contenido pertinente en notas internas. La ausencia de unidad bloquea esa línea; la asignación de máquina debe resolverse para atribuir su demanda. Completar los faltantes del paquete en paralelo sin convertirlos en supuestos.

El usuario autorizó I07 de captura rápida determinista, ya implementada y validada. I07 fue instalado por el usuario el 30 de octubre; revisar el desglose por posiciones/alias explícitos antes de guardar. Mantener controles de pedido existente/corrección y no interpretar automáticamente mensajes ambiguos. Después, el criterio siguiente es el ensayo del piloto y comparación de cantidades por fecha.

## Principios permanentes

- SQL Server es la fuente de verdad operativa; Excel es entrada de transición y evidencia.
- Producto significa variante vendible completa (tipo, número/tamaño, proceso, presentación y unidad), no sólo número de molde.
- Kilogramos, piezas, docenas y paquetes no se suman entre sí. No convertir paquetes o docenas sin una equivalencia confirmada para el producto.
- Una sugerencia o perfil no genera demanda por sí solo.
- Las correcciones preservan origen e historia y consideran lo ya producido/entregado.
- “Por producir” y “por preparar” son demanda, no prueba de ejecución física.
- Llave, ubicación interior y máquina son información interna; no deben aparecer en la vista de cliente.
- Imprimir un ticket no registra pago. Entregar usando llave tampoco registra cobro.
- Una marca `X` en un renglón de producto no cancela otros productos del cliente.

## I07 autorizado: captura rápida administrativa

La propuesta anterior fue autorizada y completada técnicamente: texto abreviado por posiciones del cliente, alias explícitos, desglose y captura manual; actualización nativa I06→I07 con respaldo, probada y empaquetada. Instalación real confirmada por el usuario el 30 de octubre. Pendiente aceptación de captura rápida en navegador; después ensayar 3–5 pedidos y una corrección 15→20 con aviso operativo.


## Ajuste autorizado I08 — 31 de octubre de 2026

Antes de ampliar operación, el usuario solicita vista amplia de configuración del cliente y días de trabajo visibles. Implementar con los endpoints/restDays existentes y entregar actualización nativa sólo del frontend. Validar calendario, conservación del perfil, medidas responsive e instalación/reversión; después confirmar aplicación del paquete y guardado semanal en VPS. No cerrar por ello la validación del guardado real de I07 ni el ensayo operativo de ruta. Contrato en INCREMENTO-08.md.

## Ajuste autorizado I09 — 31 de octubre de 2026

El usuario autoriza enlace para el próximo pedido mediante los tokens existentes, teléfono requerido al configurar el recordatorio, mensaje editable y apertura individual de WhatsApp. La fecha automática será hoy antes de las 18:00 y mañana desde las 18:00, según servidor en Ciudad de México; confirmar después del corte requiere revisar la nueva fecha sin perder la captura. Llevar la captura rápida/manual I07 al portal del cliente con aislamiento, productos/unidades confirmados, idempotencia y vigencia/revocación del acceso comprobadas en servidor. Mantener las reglas de revisión, fechas explícitas e historia existentes.

Este paso refuerza captura de etapa 1 y permite probar el mensaje sin adelantar la integración oficial de etapa 5. No incluye envío masivo, horarios por vendedor, jobs ni comprobantes de envío. Una futura selección automática deberá consultar pedido/NoOrder/descanso para la fecha de entrega calculada, no sólo para la fecha calendario actual. La entrega nativa incluye backend/frontend y conserva configuración, cuentas, catálogos, canal reparado y esquema vigente. Contrato y criterios en `INCREMENTO-09.md`; despliegue y aceptación operativa siguen siendo hitos separados.
