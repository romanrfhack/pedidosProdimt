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
